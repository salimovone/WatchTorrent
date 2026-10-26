using System.Diagnostics;
using TorrentReader.Http;
using WatchTorrent.Services;

namespace WatchTorrent.Views
{
    /// <summary>
    /// Saytni WebView'da ochib, Cloudflare tekshiruvi tugashini kutadi. Har soniyada sahifa holati va
    /// cookie'lar tekshiriladi; tayyor bo'lgach <see cref="Result"/> cookie + User-Agent bilan yakunlanadi.
    /// </summary>
    public partial class CloudflarePage : ContentPage
    {
        // 'complete' kutilmaydi: saytdagi reklama skriptlari tufayli sahifa ko'pincha unga yetmaydi.
        // 'interactive' da DOM to'liq o'qilgan va challenge sahifasining inline skriptlari bajarilgan bo'ladi.
        private const string PageStateScript =
            "(function () {" +
            "  if (document.readyState === 'loading') return 'loading';" +
            "  var challenge = typeof window._cf_chl_opt !== 'undefined'" +
            "    || document.title.indexOf('Just a moment') >= 0" +
            "    || !!document.querySelector('#challenge-form, #challenge-stage, #challenge-running, .cf-turnstile');" +
            "  return challenge ? 'challenge' : 'ready';" +
            "})()";

        // cf_clearance bo'lmasa ham sahifa shuncha tekshiruvda "tayyor" tursa — tekshiruv talab qilinmagan.
        private const int ReadyChecksWithoutClearance = 3;

        // Tekshiruv odatda bir necha soniyada o'tadi; shundan uzoq cho'zilsa yordam paneli ko'rsatiladi.
        private static readonly TimeSpan HelpDelay = TimeSpan.FromSeconds(20);

        private readonly Uri _url;
        private readonly TaskCompletionSource<CloudflareClearance?> _result = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly IDispatcherTimer _timer;
        private bool _isChecking;
        private int _readyChecks;
        private DateTime _attemptStartedAt = DateTime.UtcNow;

        public CloudflarePage(Uri url)
        {
            InitializeComponent();

            _url = url;

            // Sahifa platforma WebView'i sozlangandan keyin ochiladi (third-party cookie ruxsati yuklanishdan oldin kerak).
            Browser.HandlerChanged += OnBrowserHandlerChanged;

            _timer = Dispatcher.CreateTimer();
            _timer.Interval = TimeSpan.FromSeconds(1);
            _timer.Tick += OnTimerTick;
        }

        public Task<CloudflareClearance?> Result => _result.Task;

        protected override void OnAppearing()
        {
            base.OnAppearing();

            _timer.Start();
        }

        protected override void OnDisappearing()
        {
            base.OnDisappearing();

            _timer.Stop();
            _result.TrySetResult(null);
        }

        private void OnCancelClicked(object? sender, EventArgs e)
        {
            _result.TrySetResult(null);
        }

        private void OnBrowserHandlerChanged(object? sender, EventArgs e)
        {
            if (Browser.Handler == null || Browser.Source != null)
            {
                return;
            }

            WebViewCookies.AllowThirdPartyCookies(Browser);
            Navigate();
        }

        private void OnReloadClicked(object? sender, EventArgs e)
        {
            Navigate();
        }

        /// <summary>Eskirgan Cloudflare cookie'lari (boshqa tarmoq yoki WebView versiyasida olingan) tozalanadi.</summary>
        private async void OnResetClicked(object? sender, EventArgs e)
        {
            try
            {
                await WebViewCookies.ClearAsync(Browser);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[Cloudflare] WebView tozalanmadi: {ex}");
            }

            Navigate();
        }

        private void Navigate()
        {
            _readyChecks = 0;
            _attemptStartedAt = DateTime.UtcNow;

            // Har safar yangi obyekt — aks holda bir xil manzil qayta ochilmaydi.
            Browser.Source = new UrlWebViewSource { Url = _url.ToString() };
        }

        private void ShowHelp()
        {
            if (HelpPanel.IsVisible)
            {
                return;
            }

            if (WebViewCookies.GetEngineInfo() is { } engine)
            {
                EngineLabel.Text = $"WebView: {engine}";
                EngineLabel.IsVisible = true;
            }

            HelpPanel.IsVisible = true;
        }

        private async void OnTimerTick(object? sender, EventArgs e)
        {
            if (_isChecking || _result.Task.IsCompleted)
            {
                return;
            }

            _isChecking = true;

            try
            {
                var state = await Browser.EvaluateJavaScriptAsync(PageStateScript);

                if (state?.Contains("ready") != true)
                {
                    _readyChecks = 0;

                    if (DateTime.UtcNow - _attemptStartedAt > HelpDelay)
                    {
                        ShowHelp();
                    }

                    return;
                }

                var cookies = await WebViewCookies.GetAsync(Browser, _url);

                if (!cookies.Contains("cf_clearance=") && ++_readyChecks < ReadyChecksWithoutClearance)
                {
                    return;
                }

                var userAgent = await Browser.EvaluateJavaScriptAsync("navigator.userAgent");

                if (!string.IsNullOrEmpty(userAgent))
                {
                    _result.TrySetResult(new CloudflareClearance(userAgent, cookies));
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[Cloudflare] Tekshirishda xato: {ex}");
            }
            finally
            {
                _isChecking = false;
            }
        }
    }
}
