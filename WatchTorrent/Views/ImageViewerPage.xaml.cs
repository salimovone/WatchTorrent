namespace WatchTorrent.Views
{
    /// <summary>Posterni to'liq ekranda ko'rsatadi; kattalashtirish va surish mumkin.</summary>
    public partial class ImageViewerPage : ContentPage, IQueryAttributable
    {
        private const double MaxScale = 5;
        private const double DoubleTapScale = 2.5;

        private double _scale = 1;
        private double _panStartX;
        private double _panStartY;

        public ImageViewerPage()
        {
            InitializeComponent();

            Picture.PropertyChanged += (_, e) =>
            {
                if (e.PropertyName == nameof(Image.IsLoading))
                {
                    Loader.IsRunning = Loader.IsVisible = Picture.IsLoading;
                }
            };
        }

        public void ApplyQueryAttributes(IDictionary<string, object> query)
        {
            if (query.TryGetValue("Title", out var title))
            {
                TitleLabel.Text = title as string;
            }

            if (query.TryGetValue("Url", out var url) && url is string text && Uri.TryCreate(text, UriKind.Absolute, out var uri))
            {
                Picture.Source = ImageSource.FromUri(uri);
            }
        }

        private async void OnCloseClicked(object? sender, EventArgs e)
        {
            await Shell.Current.GoToAsync("..");
        }

        private void OnPinchUpdated(object? sender, PinchGestureUpdatedEventArgs e)
        {
            if (e.Status == GestureStatus.Running)
            {
                // e.Scale — oldingi holatga nisbatan o'zgarish.
                SetScale(_scale * e.Scale);
            }
            else if (e.Status == GestureStatus.Completed && _scale < 1.05)
            {
                SetScale(1);
            }
        }

        private void OnPanUpdated(object? sender, PanUpdatedEventArgs e)
        {
            if (_scale <= 1)
            {
                return;
            }

            switch (e.StatusType)
            {
                case GestureStatus.Started:
                    _panStartX = Picture.TranslationX;
                    _panStartY = Picture.TranslationY;
                    break;
                case GestureStatus.Running:
                    Picture.TranslationX = _panStartX + e.TotalX;
                    Picture.TranslationY = _panStartY + e.TotalY;
                    ClampTranslation();
                    break;
            }
        }

        private void OnDoubleTapped(object? sender, TappedEventArgs e)
        {
            SetScale(_scale > 1 ? 1 : DoubleTapScale);
        }

        private void SetScale(double scale)
        {
            _scale = Math.Clamp(scale, 1, MaxScale);
            Picture.Scale = _scale;

            if (_scale <= 1)
            {
                Picture.TranslationX = 0;
                Picture.TranslationY = 0;
            }

            ClampTranslation();
        }

        /// <summary>Kattalashtirilgan rasm ekrandan butunlay chiqib ketmasligi uchun.</summary>
        private void ClampTranslation()
        {
            var maxX = Math.Max(0, (ZoomHost.Width * _scale - ZoomHost.Width) / 2);
            var maxY = Math.Max(0, (ZoomHost.Height * _scale - ZoomHost.Height) / 2);

            Picture.TranslationX = Math.Clamp(Picture.TranslationX, -maxX, maxX);
            Picture.TranslationY = Math.Clamp(Picture.TranslationY, -maxY, maxY);
        }
    }
}
