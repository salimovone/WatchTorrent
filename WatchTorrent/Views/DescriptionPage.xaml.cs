namespace WatchTorrent.Views
{
    /// <summary>Torrent tavsifi (saytdagi HTML) — qorong'i temaga moslab WebView'da ko'rsatiladi.</summary>
    public partial class DescriptionPage : ContentPage, IQueryAttributable
    {
        // Tavsifdagi nisbiy rasm/havolalar shu manzilga nisbatan ochiladi.
        private const string BaseUrl = "https://1337x.to/";

        public DescriptionPage()
        {
            InitializeComponent();
        }

        public void ApplyQueryAttributes(IDictionary<string, object> query)
        {
            if (query.TryGetValue("Title", out var title) && title is string text)
            {
                Title = text;
            }

            if (query.TryGetValue("Html", out var html))
            {
                Browser.Source = new HtmlWebViewSource { Html = Wrap(html as string ?? string.Empty), BaseUrl = BaseUrl };
            }
        }

        private void OnNavigating(object? sender, WebNavigatingEventArgs e)
        {
            // Tavsifdagi havolalar ilova ichida emas, tizim brauzerida ochiladi.
            if (e.NavigationEvent == WebNavigationEvent.NewPage
                && e.Url != BaseUrl
                && Uri.TryCreate(e.Url, UriKind.Absolute, out var uri)
                && (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps))
            {
                e.Cancel = true;
                _ = Launcher.Default.TryOpenAsync(uri);
            }
        }

        private static string Wrap(string body)
        {
            // Sayt rasmlarni "lazy" yuklaydi (data-original), shuning uchun haqiqiy manzil src'ga ko'chiriladi.
            return $$"""
                <!DOCTYPE html>
                <html>
                <head>
                    <meta charset="utf-8" />
                    <meta name="viewport" content="width=device-width, initial-scale=1" />
                    <style>
                        body { background: #0E0F13; color: #E6E8EE; font-family: -apple-system, 'Segoe UI', Roboto, sans-serif;
                               font-size: 15px; line-height: 1.55; margin: 0; padding: 14px; word-wrap: break-word; }
                        img { max-width: 100%; height: auto; border-radius: 8px; }
                        a { color: #9D86FF; }
                        pre, code { white-space: pre-wrap; }
                    </style>
                </head>
                <body>
                {{body}}
                <script>
                    document.querySelectorAll('img[data-original]').forEach(function (img) {
                        img.src = img.getAttribute('data-original');
                    });
                </script>
                </body>
                </html>
                """;
        }
    }
}
