using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using HtmlAgilityPack;
using Newtonsoft.Json;
using TorrentReader.Config;

namespace TorrentReader.Http
{
    /// <summary>
    /// Barcha so'rovlar uchun bitta HttpClient. Cloudflare challenge aniqlansa,
    /// solver (MAUI'da WebView) bir marta chaqiriladi, cookie saqlanadi va so'rov qayta yuboriladi.
    /// </summary>
    internal class HttpProvider : IHttpProvider
    {
        private const string DefaultUserAgent =
            "Mozilla/5.0 (Linux; Android 14) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/130.0.0.0 Mobile Safari/537.36";

        private readonly Func<Uri, Task<CloudflareClearance>> _solver;
        private readonly SemaphoreSlim _solveLock = new SemaphoreSlim(1, 1);
        private readonly CookieContainer _cookies = new CookieContainer();
        private readonly HttpClient _httpClient;

        private volatile CloudflareClearance _clearance;

        public HttpProvider(Func<Uri, Task<CloudflareClearance>> solver = null)
        {
            _solver = solver;

            // HttpClientHandler MAUI'da platformaning native handleriga (Android: AndroidMessageHandler) o'tadi.
            var handler = new HttpClientHandler
            {
                CookieContainer = _cookies,
                UseCookies = true,
                AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate
            };

            _httpClient = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(30) };
        }

        public CloudflareClearance Clearance
        {
            get => _clearance;
            set
            {
                if (value != null)
                {
                    AddCookies(value.Cookies);
                }

                _clearance = value;
            }
        }

        public async Task<HtmlDocument> GetHtmlAsync(string url)
        {
            var html = await GetStringAsync(new Uri(url)).ConfigureAwait(false);

            var document = new HtmlDocument();
            document.LoadHtml(html);

            return document;
        }

        public async Task<IReadOnlyList<TEntity>> GetAsync<TEntity>(string relativeUrl) where TEntity : class
        {
            var jsonBody = await GetStringAsync(new Uri($"{Configuration.BaseUrl}{relativeUrl}")).ConfigureAwait(false);

            return JsonConvert.DeserializeObject<IReadOnlyList<TEntity>>(jsonBody);
        }

        private async Task<string> GetStringAsync(Uri url)
        {
            var clearanceBefore = _clearance;

            var body = await SendAsync(url).ConfigureAwait(false);

            if (body != null)
            {
                return body;
            }

            if (_solver == null)
            {
                throw new CloudflareChallengeException(url);
            }

            await _solveLock.WaitAsync().ConfigureAwait(false);

            try
            {
                // Parallel so'rovlardan biri allaqachon hal qilgan bo'lsa, WebView qayta ochilmaydi.
                if (ReferenceEquals(_clearance, clearanceBefore))
                {
                    Clearance = await _solver(url).ConfigureAwait(false) ?? throw new CloudflareChallengeException(url);
                }
            }
            finally
            {
                _solveLock.Release();
            }

            return await SendAsync(url).ConfigureAwait(false) ?? throw new CloudflareChallengeException(url);
        }

        /// <summary>Sahifa matnini qaytaradi, Cloudflare challenge bo'lsa null.</summary>
        private async Task<string> SendAsync(Uri url)
        {
            using (var request = new HttpRequestMessage(HttpMethod.Get, url))
            {
                request.Headers.TryAddWithoutValidation("User-Agent", _clearance?.UserAgent ?? DefaultUserAgent);
                request.Headers.TryAddWithoutValidation("Accept", "text/html,application/xhtml+xml,application/xml;q=0.9,*/*;q=0.8");
                request.Headers.TryAddWithoutValidation("Accept-Language", "en-US,en;q=0.9");

                using (var response = await _httpClient.SendAsync(request).ConfigureAwait(false))
                {
                    var body = await response.Content.ReadAsStringAsync().ConfigureAwait(false);

                    if (IsChallenge(response, body))
                    {
                        return null;
                    }

                    response.EnsureSuccessStatusCode();

                    return body;
                }
            }
        }

        private static bool IsChallenge(HttpResponseMessage response, string body)
        {
            if (response.Headers.Contains("cf-mitigated"))
            {
                return true;
            }

            var status = (int)response.StatusCode;

            return (status == 403 || status == 503)
                && (body.Contains("challenge-platform") || body.Contains("challenge-error-text") || body.Contains("_cf_chl_opt"));
        }

        private void AddCookies(string cookies)
        {
            if (string.IsNullOrEmpty(cookies))
            {
                return;
            }

            var baseUri = new Uri(Configuration.BaseUrl);

            foreach (var pair in cookies.Split(';'))
            {
                var separatorIndex = pair.IndexOf('=');

                if (separatorIndex <= 0)
                {
                    continue;
                }

                var name = pair.Substring(0, separatorIndex).Trim();
                var value = pair.Substring(separatorIndex + 1).Trim();

                _cookies.Add(baseUri, new Cookie(name, value, "/", baseUri.Host));
            }
        }
    }
}
