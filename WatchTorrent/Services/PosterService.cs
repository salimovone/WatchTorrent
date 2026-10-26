using System.Collections.Concurrent;
using System.Diagnostics;
using System.Net.Http;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace WatchTorrent.Services
{
    /// <summary>Topilgan poster. IMDb rasmlarini URL orqali kerakli kenglikda olish mumkin.</summary>
    public record PosterInfo(string ImageUrl)
    {
        /// <summary>IMDb (m.media-amazon.com) rasmini <paramref name="width"/> px kenglikda qaytaradi; boshqa manzillar o'zgarmaydi.</summary>
        public string Sized(int width)
        {
            return ImageUrl.EndsWith("._V1_.jpg", StringComparison.Ordinal)
                ? ImageUrl.Replace("._V1_.jpg", $"._V1_QL80_UX{width}_.jpg")
                : ImageUrl;
        }
    }

    /// <summary>
    /// 1337x ro'yxatlarida rasm yo'q, shuning uchun poster torrent nomidan ajratilgan sarlavha/yil bo'yicha
    /// IMDb qidiruvidan (kalitsiz ochiq endpoint) olinadi. Natijalar diskda keshlanadi.
    /// </summary>
    public class PosterService
    {
        private const int MaxConcurrentRequests = 4;

        private static readonly HttpClient Http = CreateHttpClient();

        private readonly ConcurrentDictionary<string, Task<PosterInfo?>> _lookups = new();
        private readonly SemaphoreSlim _throttle = new(MaxConcurrentRequests);
        private readonly object _cacheLock = new();
        private readonly string _cachePath = Path.Combine(FileSystem.CacheDirectory, "posters.json");
        private Dictionary<string, string>? _cache;
        private bool _saveScheduled;

        public Task<PosterInfo?> FindAsync(string torrentName)
        {
            var title = TorrentTitle.Parse(torrentName);

            return title == null ? Task.FromResult<PosterInfo?>(null) : _lookups.GetOrAdd(title.Key, _ => LookupAsync(title));
        }

        private async Task<PosterInfo?> LookupAsync(TorrentTitle title)
        {
            if (TryGetCached(title.Key, out var cached))
            {
                return cached.Length > 0 ? new PosterInfo(cached) : null;
            }

            await _throttle.WaitAsync();

            try
            {
                var query = title.Query;
                var first = char.ToLowerInvariant(query.FirstOrDefault(char.IsLetterOrDigit));
                var url = $"https://v3.sg.media-imdb.com/suggestion/{(first == default ? 'x' : first)}/{Uri.EscapeDataString(query)}.json";

                using var document = JsonDocument.Parse(await Http.GetStringAsync(url));

                var imageUrl = PosterMatcher.PickBest(document.RootElement, title);

                // Topilmagan natija ham keshlanadi — aks holda har safar qayta so'raladi.
                Remember(title.Key, imageUrl ?? string.Empty);

                return imageUrl != null ? new PosterInfo(imageUrl) : null;
            }
            catch (Exception ex)
            {
                // Tarmoq xatosi keshlanmaydi: keyingi safar qayta urinib ko'riladi.
                _lookups.TryRemove(title.Key, out _);
                Debug.WriteLine($"[Posters] {title.Query}: {ex.Message}");

                return null;
            }
            finally
            {
                _throttle.Release();
            }
        }

        private bool TryGetCached(string key, out string imageUrl)
        {
            lock (_cacheLock)
            {
                _cache ??= LoadCache();

                return _cache.TryGetValue(key, out imageUrl!);
            }
        }

        private void Remember(string key, string imageUrl)
        {
            lock (_cacheLock)
            {
                _cache ??= LoadCache();
                _cache[key] = imageUrl;

                if (_saveScheduled)
                {
                    return;
                }

                _saveScheduled = true;
            }

            // Bir ro'yxatdagi o'nlab natija bitta yozuvda saqlanadi.
            _ = Task.Run(async () =>
            {
                await Task.Delay(TimeSpan.FromSeconds(3));

                string json;

                lock (_cacheLock)
                {
                    _saveScheduled = false;
                    json = JsonSerializer.Serialize(_cache!, PosterCacheJsonContext.Default.DictionaryStringString);
                }

                try
                {
                    await File.WriteAllTextAsync(_cachePath, json);
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"[Posters] Kesh saqlanmadi: {ex.Message}");
                }
            });
        }

        private Dictionary<string, string> LoadCache()
        {
            try
            {
                if (File.Exists(_cachePath))
                {
                    return JsonSerializer.Deserialize(File.ReadAllText(_cachePath), PosterCacheJsonContext.Default.DictionaryStringString) ?? new();
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[Posters] Kesh o'qilmadi: {ex.Message}");
            }

            return new Dictionary<string, string>();
        }

        private static HttpClient CreateHttpClient()
        {
            var http = new HttpClient { Timeout = TimeSpan.FromSeconds(10) };
            http.DefaultRequestHeaders.TryAddWithoutValidation("User-Agent", "Mozilla/5.0");

            return http;
        }
    }

    /// <summary>Release build'da trimming reflection asosidagi JSON'ni o'chiradi — kesh uchun source generator.</summary>
    [JsonSerializable(typeof(Dictionary<string, string>))]
    internal partial class PosterCacheJsonContext : JsonSerializerContext
    {
    }
}
