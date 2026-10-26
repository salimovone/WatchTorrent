using WatchTorrent.Services;
using WatchTorrent.ViewModels;

namespace WatchTorrent.Views
{
    public partial class PlayerPage : ContentPage
    {
        private const int DoubleTapMs = 300;
        private const double SeekStepSeconds = 10;

        private static readonly TimeSpan ControlsTimeout = TimeSpan.FromSeconds(4);

        private readonly PlayerViewModel _viewModel;
        private readonly IDispatcherTimer _timer;
        private CancellationTokenSource? _hideControls;
        private CancellationTokenSource? _singleTap;
        private long _lastTapAt;
        private double _lastTapX;
        private bool _isDraggingSeek;
        private bool _isUpdatingSeekBar;
        private bool _started;
        private bool _leaving;
        private bool _isShutDown;
        private Window? _window;

        public PlayerPage(PlayerViewModel viewModel)
        {
            InitializeComponent();

            BindingContext = _viewModel = viewModel;
            _viewModel.CloseRequested += async (_, _) => await LeaveAsync();

            FullScreenButton.IsVisible = PlayerWindow.CanToggleFullScreen;

            _timer = Dispatcher.CreateTimer();
            _timer.Interval = TimeSpan.FromMilliseconds(500);
            _timer.Tick += (_, _) =>
            {
                _viewModel.Tick();
                UpdateSeekBar();

                if (PlaylistOverlay.IsVisible)
                {
                    _viewModel.RefreshPlaylist();
                }
#if WINDOWS
                EnsureKeyboardFocus();
#endif
            };
        }

        protected override async void OnAppearing()
        {
            base.OnAppearing();

            if (_started)
            {
                return;
            }

            _started = true;

            PlayerWindow.Enter();

            // Ilova fonga o'tsa film pauza qilinadi.
            _window = Window;

            if (_window != null)
            {
                _window.Stopped += OnWindowStopped;
            }

            _timer.Start();
            ShowControls();
#if WINDOWS
            AttachKeyboard();
#endif

            try
            {
                _viewModel.CreatePlayer(await GetPlatformOptionsAsync());
                Video.MediaPlayer = _viewModel.Player;
                ReportVideoSurfaceSize();

                _viewModel.SetScreenSize(Video.Width, Video.Height);
                Video.SizeChanged += (_, _) => _viewModel.SetScreenSize(Video.Width, Video.Height);

                await _viewModel.StartAsync();
            }
            catch (Exception ex)
            {
                _viewModel.ShowError(ex);
            }
        }

        /// <summary>
        /// LibVLCSharp (Android) VLC'ga oyna o'lchamini faqat layout o'zgarganda beradi. Player view joylashib
        /// bo'lgandan keyin ulanadi, shuning uchun layout bir marta "silkitiladi" — aks holda VLC oynani video
        /// o'lchamida deb hisoblab, rasmni chap burchakka va noto'g'ri formatda chizadi.
        /// </summary>
        private void ReportVideoSurfaceSize()
        {
#if ANDROID
            if (Video.Handler?.PlatformView is Android.Views.View view && view.Width > 1)
            {
                view.Layout(view.Left, view.Top, view.Right - 1, view.Bottom);
                view.RequestLayout();
            }
#endif
        }

        protected override async void OnDisappearing()
        {
            base.OnDisappearing();

            await ShutDownAsync();
        }

        protected override bool OnBackButtonPressed()
        {
            // Fayllar ro'yxati ochiq bo'lsa "orqaga" avval uni yopadi.
            if (PlaylistOverlay.IsVisible)
            {
                HidePlaylist();
            }
            else
            {
                _ = LeaveAsync();
            }

            return true;
        }

        /// <summary>Avval player to'xtatiladi (VLC va torrent stream toza yopilsin), keyin sahifa yopiladi.</summary>
        private async Task LeaveAsync()
        {
            if (_leaving)
            {
                return;
            }

            _leaving = true;

            await ShutDownAsync();
            await Shell.Current.GoToAsync("..");
        }

        private async Task ShutDownAsync()
        {
            if (_isShutDown)
            {
                return;
            }

            _isShutDown = true;

            if (_window != null)
            {
                _window.Stopped -= OnWindowStopped;
            }

            _timer.Stop();
            _hideControls?.Cancel();
#if WINDOWS
            DetachKeyboard();
#endif

            // Tartib muhim: avval VLC to'xtatilib video sirtidan ajratiladi, keyin ekran yo'nalishi qaytariladi.
            // Aksincha bo'lsa, burilish paytida qayta yaratilayotgan sirtga chizish native crash beradi.
            await _viewModel.StopPlaybackAsync();

            Video.MediaPlayer = null;
            _viewModel.DisposePlayer();
            PlayerWindow.Exit();

            // Torrentni tez yuklash rejimiga qaytarish bir-ikki soniya oladi — sahifa yopilishini kutdirmaydi.
            _ = _viewModel.ReleaseTorrentAsync();
        }

        private async Task<string[]> GetPlatformOptionsAsync()
        {
#if WINDOWS
            // WinUI'da video VideoView yaratgan swapchain'ga chiziladi — uning parametrlari LibVLC'ga berilishi shart.
            for (var attempt = 0; attempt < 50; attempt++)
            {
                try
                {
                    if (Video.Handler?.PlatformView is LibVLCSharp.Platforms.Windows.VideoViewBase view && view.SwapChainOptions is { Length: > 0 } options)
                    {
                        return options.ToArray();
                    }
                }
                catch (InvalidOperationException)
                {
                    // VideoView hali yuklanmagan.
                }

                await Task.Delay(100);
            }
#else
            // Android'da video sirti (Surface) tayyor bo'lishini kutamiz.
            for (var attempt = 0; attempt < 30 && (Video.Handler == null || Video.Width <= 0); attempt++)
            {
                await Task.Delay(100);
            }
#endif

            return Array.Empty<string>();
        }

        private void OnWindowStopped(object? sender, EventArgs e)
        {
            _viewModel.Pause();
        }

        private async void OnSurfaceTapped(object? sender, TappedEventArgs e)
        {
            var x = e.GetPosition(TouchLayer)?.X ?? TouchLayer.Width / 2;
            var now = Environment.TickCount64;

            if (now - _lastTapAt < DoubleTapMs && GetZone(x) == GetZone(_lastTapX))
            {
                _singleTap?.Cancel();
                _lastTapAt = 0;
                OnDoubleTap(x);

                return;
            }

            _lastTapAt = now;
            _lastTapX = x;

            // Ikkinchi bosish kelmasa — oddiy bosish: boshqaruvni ko'rsatish/yashirish.
            _singleTap?.Cancel();
            var singleTap = _singleTap = new CancellationTokenSource();

            try
            {
                await Task.Delay(DoubleTapMs, singleTap.Token);

                if (Controls.IsVisible)
                {
                    HideControls();
                }
                else
                {
                    ShowControls();
                }
            }
            catch (TaskCanceledException)
            {
            }
        }

        private void OnDoubleTap(double x)
        {
            switch (GetZone(x))
            {
                case < 0:
                    _viewModel.SeekBy(-SeekStepSeconds);
                    _ = ShowSeekFeedbackAsync("« 10 s", LayoutOptions.Start);
                    break;
                case > 0:
                    _viewModel.SeekBy(SeekStepSeconds);
                    _ = ShowSeekFeedbackAsync("10 s »", LayoutOptions.End);
                    break;
                default:
                    _viewModel.TogglePlay();
                    ShowControls();
                    break;
            }
        }

        /// <summary>Ekran uch qismga bo'linadi: chap (-1), o'rta (0), o'ng (1).</summary>
        private int GetZone(double x)
        {
            var width = TouchLayer.Width;

            return x < width / 3 ? -1 : x > width * 2 / 3 ? 1 : 0;
        }

        private async Task ShowSeekFeedbackAsync(string text, LayoutOptions side)
        {
            SeekFeedback.Text = text;
            SeekFeedback.HorizontalOptions = side;
            SeekFeedback.Opacity = 1;

            await Task.Delay(600);

            if (SeekFeedback.Text == text)
            {
                SeekFeedback.Opacity = 0;
            }
        }

        private async Task ShowOsdAsync(string text)
        {
            OsdText.Text = text;
            Osd.Opacity = 1;

            await Task.Delay(1500);

            // Shu orada yangi xabar chiqqan bo'lsa, uni yashirmaymiz.
            if (OsdText.Text == text)
            {
                Osd.Opacity = 0;
            }
        }

        private void ShowControls()
        {
            Controls.IsVisible = true;
            ScheduleHideControls();
        }

        private void HideControls()
        {
            _hideControls?.Cancel();
            Controls.IsVisible = false;
        }

        private async void ScheduleHideControls()
        {
            _hideControls?.Cancel();
            var hideControls = _hideControls = new CancellationTokenSource();

            try
            {
                while (true)
                {
                    await Task.Delay(ControlsTimeout, hideControls.Token);

                    // Pauza, yuklanish va seek paytida boshqaruv ko'rinib turadi.
                    if (_viewModel.IsPlaying && !_viewModel.IsLoading && !_isDraggingSeek)
                    {
                        Controls.IsVisible = false;

                        return;
                    }
                }
            }
            catch (TaskCanceledException)
            {
            }
        }

        private async void OnBackClicked(object? sender, EventArgs e)
        {
            await LeaveAsync();
        }

        private void OnPlayPauseClicked(object? sender, EventArgs e)
        {
            ShowControls();
            _viewModel.TogglePlay();
        }

        private void OnRewindClicked(object? sender, EventArgs e)
        {
            ShowControls();
            _viewModel.SeekBy(-SeekStepSeconds);
        }

        private void OnForwardClicked(object? sender, EventArgs e)
        {
            ShowControls();
            _viewModel.SeekBy(SeekStepSeconds);
        }

        private async void OnAudioClicked(object? sender, EventArgs e)
        {
            ShowControls();
            await _viewModel.ChooseAudioAsync();
        }

        private async void OnSubtitlesClicked(object? sender, EventArgs e)
        {
            ShowControls();
            await _viewModel.ChooseSubtitleAsync();
        }

        private void OnAspectClicked(object? sender, EventArgs e)
        {
            ShowControls();
            _ = ShowOsdAsync($"Ekran formati: {_viewModel.CycleAspectMode()}");
        }

        private async void OnSpeedClicked(object? sender, EventArgs e)
        {
            ShowControls();
            await _viewModel.ChooseSpeedAsync();
        }

        private void OnFullScreenClicked(object? sender, EventArgs e)
        {
            ShowControls();
            PlayerWindow.ToggleFullScreen();
        }

        private void OnRestartClicked(object? sender, EventArgs e)
        {
            _viewModel.RestartFromBeginning();
        }

        private void OnSeekDragStarted(object? sender, EventArgs e)
        {
            _isDraggingSeek = true;
            _viewModel.BeginSeek();
            ShowControls();
        }

        private void OnSeekDragCompleted(object? sender, EventArgs e)
        {
            _isDraggingSeek = false;
            _viewModel.EndSeek(SeekBar.Value);
            ShowControls();
        }

        private void OnSeekValueChanged(object? sender, ValueChangedEventArgs e)
        {
            if (_isUpdatingSeekBar)
            {
                return;
            }

            if (_isDraggingSeek)
            {
                _viewModel.PreviewSeek(e.NewValue);
            }
            else
            {
                // Chiziqning o'ziga bosildi (sudrashsiz) — o'sha joyga o'tiladi.
                _viewModel.SeekTo(e.NewValue);
                ShowControls();
            }
        }

        private void UpdateSeekBar()
        {
            if (_isDraggingSeek)
            {
                return;
            }

            _isUpdatingSeekBar = true;

            if (Math.Abs(SeekBar.Maximum - _viewModel.DurationSeconds) > 0.5)
            {
                SeekBar.Maximum = _viewModel.DurationSeconds;
            }

            SeekBar.Value = Math.Min(_viewModel.PositionSeconds, SeekBar.Maximum);

            _isUpdatingSeekBar = false;
        }

        private async void OnNextClicked(object? sender, EventArgs e)
        {
            await PlayNextAsync();
        }

        private async Task PlayNextAsync()
        {
            if (!_viewModel.HasNext)
            {
                return;
            }

            ShowControls();
            await _viewModel.PlayNextAsync();
            _ = ShowOsdAsync(_viewModel.Title);
        }

        private void OnPlaylistClicked(object? sender, EventArgs e)
        {
            _viewModel.RefreshPlaylist();
            PlaylistOverlay.IsVisible = true;
            _hideControls?.Cancel();

            if (_viewModel.Playlist.FirstOrDefault(item => item.IsCurrent) is { } current)
            {
                PlaylistView.ScrollTo(current, position: ScrollToPosition.Center, animate: false);
            }
        }

        private void OnPlaylistCloseClicked(object? sender, EventArgs e)
        {
            HidePlaylist();
        }

        private void OnPlaylistBackdropTapped(object? sender, TappedEventArgs e)
        {
            HidePlaylist();
        }

        private async void OnPlaylistSelectionChanged(object? sender, SelectionChangedEventArgs e)
        {
            if (e.CurrentSelection.FirstOrDefault() is not PlaylistItem item)
            {
                return;
            }

            PlaylistView.SelectedItem = null;
            HidePlaylist();

            if (!item.IsCurrent)
            {
                await _viewModel.SwitchToAsync(item);
                _ = ShowOsdAsync(item.Title);
            }
        }

        private void HidePlaylist()
        {
            PlaylistOverlay.IsVisible = false;
            ShowControls();
        }

#if WINDOWS
        private Microsoft.UI.Xaml.UIElement? _keyboardRoot;

        /// <summary>
        /// Windows klaviaturasi: ← / → — 10 soniya orqaga/oldinga, Space — pauza/davom, N — keyingi fayl,
        /// F11 — to'liq ekran, Esc — to'liq ekrandan chiqish. Tugmalar oynaning ildiz elementida (Preview)
        /// ushlanadi, shuning uchun fokus qaysi tugma yoki slider'da bo'lishidan qat'i nazar ishlaydi.
        /// </summary>
        private void AttachKeyboard()
        {
            if (Window?.Handler?.PlatformView is not Microsoft.UI.Xaml.Window { Content: { } root })
            {
                return;
            }

            _keyboardRoot = root;
            root.PreviewKeyDown += OnPreviewKeyDown;
            root.PreviewKeyUp += OnPreviewKeyUp;

            EnsureKeyboardFocus();
        }

        private void DetachKeyboard()
        {
            if (_keyboardRoot == null)
            {
                return;
            }

            _keyboardRoot.PreviewKeyDown -= OnPreviewKeyDown;
            _keyboardRoot.PreviewKeyUp -= OnPreviewKeyUp;
            _keyboardRoot = null;
        }

        /// <summary>
        /// Boshqaruv yashiringanda fokusdagi tugma ham yo'qoladi va klaviatura hodisalari kelmay qoladi —
        /// shunday paytda fokus oyna ildiziga qaytariladi.
        /// </summary>
        private void EnsureKeyboardFocus()
        {
            if (_keyboardRoot is not Microsoft.UI.Xaml.Controls.Control root || root.XamlRoot is not { } xamlRoot)
            {
                return;
            }

            if (Microsoft.UI.Xaml.Input.FocusManager.GetFocusedElement(xamlRoot) is Microsoft.UI.Xaml.UIElement { Visibility: Microsoft.UI.Xaml.Visibility.Visible })
            {
                return;
            }

            root.IsTabStop = true;
            root.Focus(Microsoft.UI.Xaml.FocusState.Programmatic);
        }

        private async void OnPreviewKeyDown(object sender, Microsoft.UI.Xaml.Input.KeyRoutedEventArgs e)
        {
            if (PlaylistOverlay.IsVisible)
            {
                // Ro'yxat ochiq: strelkalar ro'yxat bo'ylab yuradi, Esc uni yopadi.
                if (e.Key == global::Windows.System.VirtualKey.Escape)
                {
                    HidePlaylist();
                    e.Handled = true;
                }

                return;
            }

            switch (e.Key)
            {
                case global::Windows.System.VirtualKey.Left:
                    ShowControls();
                    _viewModel.SeekBy(-SeekStepSeconds);
                    _ = ShowSeekFeedbackAsync("« 10 s", LayoutOptions.Start);
                    break;
                case global::Windows.System.VirtualKey.Right:
                    ShowControls();
                    _viewModel.SeekBy(SeekStepSeconds);
                    _ = ShowSeekFeedbackAsync("10 s »", LayoutOptions.End);
                    break;
                case global::Windows.System.VirtualKey.Space:
                    // Tugma bosilib turilganda takrorlanmasin.
                    if (!e.KeyStatus.WasKeyDown)
                    {
                        ShowControls();
                        _viewModel.TogglePlay();
                    }

                    break;
                case global::Windows.System.VirtualKey.N:
                    if (!e.KeyStatus.WasKeyDown)
                    {
                        await PlayNextAsync();
                    }

                    break;
                case global::Windows.System.VirtualKey.F11:
                    PlayerWindow.ToggleFullScreen();
                    break;
                case global::Windows.System.VirtualKey.Escape when PlayerWindow.IsFullScreen:
                    PlayerWindow.ToggleFullScreen();
                    break;
                default:
                    return;
            }

            e.Handled = true;
        }

        /// <summary>
        /// Space fokusdagi tugmani qo'yib yuborilganda "bosadi", strelkalar slider'ni suradi — o'sha tugmalar
        /// bu yerda yutiladi, aks holda har bir buyruq ikki marta bajarilardi.
        /// </summary>
        private void OnPreviewKeyUp(object sender, Microsoft.UI.Xaml.Input.KeyRoutedEventArgs e)
        {
            if (!PlaylistOverlay.IsVisible && e.Key is global::Windows.System.VirtualKey.Space
                or global::Windows.System.VirtualKey.Left
                or global::Windows.System.VirtualKey.Right
                or global::Windows.System.VirtualKey.N)
            {
                e.Handled = true;
            }
        }
#endif
    }
}
