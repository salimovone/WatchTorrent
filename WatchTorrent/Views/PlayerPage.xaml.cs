using L = WatchTorrent.Core.Localization.AppText;
using WatchTorrent.Controls;
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
        private View? _menuAnchor;
        private bool _menuOpen;
        private bool _isDraggingSeek;
        private bool _isUpdatingSeekBar;
        private bool _controlsVisible = true;
        private bool _started;
        private bool _leaving;
        private bool _isShutDown;
        private Window? _window;
        private Services.VoiceCall? call;
        private void RefreshMic()
        {
            MicButton.IsVisible = call?.Connected == true;
            bool on = call?.Microphone == true;
            MicButton.Source = on ? "ic_mic_on.png" : "ic_mic_off.png";
            SemanticProperties.SetDescription(MicButton, on ? L.Get("Mikrofonni o‘chirish") : L.Get("Mikrofonni yoqish"));
        }
        private async void OnMicClicked(object? sender, EventArgs args)
        {
            if (call != null) await call.ToggleMicrophoneAsync();
        }

        public PlayerPage(PlayerViewModel viewModel)
        {
            InitializeComponent();

            BindingContext = _viewModel = viewModel;
            _viewModel.CloseRequested += async (_, _) => await LeaveAsync();
            _viewModel.PlayerChanged += OnPlayerChanged;
            _viewModel.SelectOptionAsync = SelectPlayerOptionAsync;
#if ANDROID
            _viewModel.WaitForVideoSurface = () => _videoSurfaceReady;
#endif

            FullScreenButton.IsVisible = PlayerWindow.CanToggleFullScreen;
            PipButton.IsVisible = PictureInPicture.IsSupported;
            // All surface gestures use the same state machine; buttons keep their own input.
            TouchLayer.GestureRecognizers.Clear();
            TouchLayer.HandlerChanged += (_, _) => AttachTouch();
            TouchLayer.HandlerChanging += (_, _) => DetachTouch();
#if ANDROID
            // HDR rejimida subtitrlar video ustidagi alohida sirtga chiziladi (DirectVideoOutput).
            VideoHost.Children.Insert(VideoHost.Children.IndexOf(Video) + 1, _subtitleLayer);
            VideoHost.SizeChanged += (_, _) => ApplyVideoLayout();
#endif

            _timer = Dispatcher.CreateTimer();
            _timer.Interval = TimeSpan.FromMilliseconds(500);
            _timer.Tick += (_, _) =>
            {
                _viewModel.Tick();
                PartnerBuffer.IsVisible = PartnerSpinner.IsRunning = _viewModel.Session?.RemoteLoading == true && !_viewModel.ShowSpinner && !_viewModel.HasError;
                UpdateSeekBar();

                if (PictureInPicture.IsSupported)
                {
                    PictureInPicture.Update(_viewModel.IsPlaying, _viewModel.VideoAspectRatio);
                }

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
            call = ((AppShell)Shell.Current).Call;
            call.Changed += RefreshMic;
            RefreshMic();

            PlayerWindow.Enter();

            PictureInPicture.Enable();
            PictureInPicture.Changed += OnPictureInPictureChanged;
            PictureInPicture.CommandReceived += OnPictureInPictureCommand;

            // Ilova fonga o'tsa (kichik oyna yopilganda ham) film pauza qilinadi. Kichik oynaga o'tish "Stopped" emas.
            // Shell orqali ochilgan sahifada Window bu paytda hali null bo'ladi — ilovaning yagona oynasi olinadi.
            _window = Window ?? Application.Current?.Windows.FirstOrDefault();

            if (_window != null)
            {
                _window.Stopped += OnWindowStopped;
                _window.Activated += OnWindowActivated;
            }

            _timer.Start();
            ShowControls();
#if WINDOWS
            AttachKeyboard();
#endif
            AttachTouch();

            try
            {
                _viewModel.CreatePlayer(await GetPlatformOptionsAsync());
                AttachVideo();

                // Ekran o'lchami — video emas, uning konteyneri (HDR rejimida video sirti nisbatga moslab kichraytiriladi).
                _viewModel.SetScreenSize(VideoHost.Width, VideoHost.Height);
                VideoHost.SizeChanged += (_, _) => _viewModel.SetScreenSize(VideoHost.Width, VideoHost.Height);

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
            if (Navigation.ModalStack.LastOrDefault() is MovieChoicePage) return;
            await ShutDownAsync();
        }

        protected override bool OnBackButtonPressed()
        {
            if (DropdownMenu.Dismiss(this)) return true;
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
        internal async Task LeaveAsync()
        {
            if (_leaving)
            {
                return;
            }

            _leaving = true;

            await ShutDownAsync();

            // Film kichik oynada tugadi — oynada ilovaning oldingi sahifasi ochilib qolmasin.
            PictureInPicture.Dismiss();

            await Shell.Current.GoToAsync("..");
#if ANDROID
            PlayerWindow.RefreshAppLayout();
#endif
        }

        private async Task ShutDownAsync()
        {
            if (_isShutDown)
            {
                return;
            }

            _isShutDown = true;
            _viewModel.Session?.Close();
            if (call != null) call.Changed -= RefreshMic;

            if (_window != null)
            {
                _window.Stopped -= OnWindowStopped;
                _window.Activated -= OnWindowActivated;
            }

            _timer.Stop();
            _hideControls?.Cancel();
            _singleTap?.Cancel();

            PictureInPicture.Changed -= OnPictureInPictureChanged;
            PictureInPicture.CommandReceived -= OnPictureInPictureCommand;
            PictureInPicture.Disable();
#if WINDOWS
            DetachKeyboard();
#endif
            DetachTouch();

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

        /// <summary>HDR/SDR almashganda VLC player yangisiga almashadi — video sirtiga qayta ulanadi.</summary>
        private void OnPlayerChanged(object? sender, EventArgs e)
        {
            if (_isShutDown)
            {
                return;
            }

            // Fonda (sirt yo'q) ulanmaydi — ilova qaytganda OnWindowActivated ulaydi.
            if (_viewModel.Player == null || _viewModel.IsVideoSuspended)
            {
                Video.MediaPlayer = null;

                return;
            }

            AttachVideo();
            _viewModel.SetScreenSize(VideoHost.Width, VideoHost.Height);
        }

        /// <summary>
        /// Player'ni video sirtiga ulaydi. HDR rejimida (VLC android_display) qo'shimcha ravishda video o'lchami
        /// tinglovchisi va subtitr sirti ulanadi — video sirti nisbatga moslab joylashtiriladi (<see cref="ApplyVideoLayout"/>).
        /// </summary>
        private void AttachVideo()
        {
            Video.MediaPlayer = _viewModel.Player;
#if IOS
            // VLC only renders. The app owns input, transport and overlays.
            if (Video.Handler?.PlatformView is UIKit.UIView nativeVideo)
                nativeVideo.UserInteractionEnabled = false;
#endif
#if ANDROID
            _directVideoAspect = 0;
            _videoSurfaceReady = Task.CompletedTask;

            if (_viewModel.IsDirectOutput
                && Video.Handler?.PlatformView is Android.Views.View videoView
                && _subtitleLayer.Handler?.PlatformView is Android.Views.SurfaceView subtitlesView)
            {
                // Sirt tayyor bo'lguncha (ko'pi bilan 2 s) video ochilmaydi — PlayerViewModel.WaitForVideoSurface.
                _videoSurfaceReady = Task.WhenAny(DirectVideoOutput.AttachAsync(videoView, subtitlesView, OnDirectVideoAspectChanged), Task.Delay(2000));
            }

            ApplyVideoLayout();
#endif
            ReportVideoSurfaceSize();
        }

        private void OnWindowStopped(object? sender, EventArgs e)
        {
            CancelSurfaceInput();
            _viewModel.Pause();
#if IOS
            PlayerWindow.SuspendLevels();
#endif
#if ANDROID
            // Fonda Android video sirtini yo'q qiladi: VLC Android ilovasidagidek video o'chirilib, sirtdan ajratiladi.
            if (_viewModel.SuspendVideo())
            {
                Video.MediaPlayer = null;
            }
#endif
        }

        private void OnWindowActivated(object? sender, EventArgs e)
        {
#if IOS
            PlayerWindow.ResumeLevels();
#endif
#if ANDROID
            // Ilova qaytib ochildi — yangi sirtga qayta ulanadi.
            if (_viewModel.IsVideoSuspended && !_isShutDown)
            {
                AttachVideo();
                _viewModel.ResumeVideo();
            }
#endif
        }

        private void OnSurfaceTapped(object? sender, TappedEventArgs e)
        {
            HandleTap(e.GetPosition(TouchLayer)?.X ?? TouchLayer.Width / 2);
        }

        private async void HandleTap(double x)
        {
            if (_isShutDown) return;
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
                if (_isShutDown) return;

                if (_controlsVisible)
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
            SetControlsVisible(true);
            ScheduleHideControls();
        }

        private void HideControls()
        {
            _hideControls?.Cancel();
            SetControlsVisible(false);
        }

        private void SetControlsVisible(bool visible)
        {
            // Visibility changes measure the shared video host again. Keep overlays
            // laid out when a hold begins/ends so the native video surface stays put.
            _controlsVisible = visible;
            Controls.Opacity = visible ? 1 : 0;
            Controls.CascadeInputTransparent = !visible;
            Controls.IsEnabled = visible;
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
                    if (_viewModel.IsPlaying && !_viewModel.IsLoading && !_viewModel.IsBuffering && !_isDraggingSeek && !_menuOpen)
                    {
                        SetControlsVisible(false);

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
            _menuAnchor = sender as View;
            ShowControls();
            await _viewModel.ChooseAudioAsync();
        }

        private async void OnSubtitlesClicked(object? sender, EventArgs e)
        {
            _menuAnchor = sender as View;
            ShowControls();
            await _viewModel.ChooseSubtitleAsync();
        }

        private async void OnAspectClicked(object? sender, EventArgs e)
        {
            _menuAnchor = sender as View;
            ShowControls();
            await _viewModel.ChooseAspectAsync();
        }

        private async Task<int> SelectPlayerOptionAsync(IReadOnlyList<string> options, int selected)
        {
            if (_menuAnchor == null) return -1;
            _menuOpen = true;
            _hideControls?.Cancel();
            try { return await DropdownMenu.ShowAsync(_menuAnchor, options, selected); }
            finally { _menuOpen = false; ShowControls(); }
        }

        private async void OnHdrClicked(object? sender, EventArgs e)
        {
            ShowControls();

            var text = await _viewModel.ToggleHdrAsync();
            _ = ShowOsdAsync(text);
        }

        private async void OnSpeedClicked(object? sender, EventArgs e)
        {
            _menuAnchor = sender as View;
            ShowControls();
            await _viewModel.ChooseSpeedAsync();
        }

        private void OnFullScreenClicked(object? sender, EventArgs e)
        {
            ShowControls();
            PlayerWindow.ToggleFullScreen();
        }

        private async void OnPipClicked(object? sender, EventArgs e)
        {
            HideControls();

            if (PictureInPicture.Enter())
            {
                return;
            }

            ShowControls();

            var openSettings = await DisplayAlertAsync(
                L.Get("Kichik oyna o'chirilgan"),
                L.Get("Telefon sozlamalarida WatchTorrent uchun «Picture-in-picture» (tasvir ichida tasvir) ruxsatini yoqing."),
                L.Get("Sozlamalar"),
                L.Get("Bekor qilish"));

            if (openSettings)
            {
                AppInfo.Current.ShowSettingsUI();
            }
        }

        /// <summary>Kichik oynada faqat video ko'rinadi; katta ekranga qaytganda boshqaruv ko'rsatiladi.</summary>
        private void OnPictureInPictureChanged(object? sender, EventArgs e)
        {
            Overlay.IsVisible = !PictureInPicture.IsActive;

            if (PictureInPicture.IsActive)
            {
                HideControls();
            }
            else
            {
                ShowControls();
            }
        }

        private void OnPictureInPictureCommand(object? sender, PictureInPictureCommand command)
        {
            switch (command)
            {
                case PictureInPictureCommand.Rewind:
                    _viewModel.SeekBy(-SeekStepSeconds);
                    break;
                case PictureInPictureCommand.Forward:
                    _viewModel.SeekBy(SeekStepSeconds);
                    break;
                default:
                    _viewModel.TogglePlay();
                    break;
            }

            // Oynadagi pauza/davom tugmasi taymerni kutmay almashsin.
            PictureInPicture.Update(_viewModel.IsPlaying, _viewModel.VideoAspectRatio);
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

#if ANDROID
        private readonly SubtitleSurface _subtitleLayer = new() { InputTransparent = true };
        private Task _videoSurfaceReady = Task.CompletedTask;

        // HDR rejimida videoning ekrandagi nisbati (VLC'dan); 0 — oddiy rejim, video butun konteynerni egallaydi.
        private double _directVideoAspect;

        private void OnDirectVideoAspectChanged(double aspect)
        {
            _directVideoAspect = aspect;
            ApplyVideoLayout();
        }

        /// <summary>
        /// HDR rejimida VLC rasmni kattalashtirmaydi — video va subtitr sirtlari nisbatni saqlagan holda konteynerga
        /// sig'diriladi (qora chiziqlar). Ekran formati (to'ldirish, 16:9...) VLC'dan kelgan nisbatda hisobga olingan.
        /// </summary>
        private void ApplyVideoLayout()
        {
            var width = VideoHost.Width;
            var height = VideoHost.Height;

            if (_directVideoAspect <= 0 || width <= 0 || height <= 0)
            {
                SetVideoSize(-1, -1, LayoutOptions.Fill);

                return;
            }

            var fitWidth = Math.Min(width, height * _directVideoAspect);

            SetVideoSize(fitWidth, fitWidth / _directVideoAspect, LayoutOptions.Center);
        }

        private void SetVideoSize(double width, double height, LayoutOptions options)
        {
            foreach (var view in new View[] { Video, _subtitleLayer })
            {
                view.WidthRequest = width;
                view.HeightRequest = height;
                view.HorizontalOptions = options;
                view.VerticalOptions = options;
            }
        }

#endif

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

            var focused = Microsoft.UI.Xaml.Input.FocusManager.GetFocusedElement(xamlRoot);
            if (ReferenceEquals(focused, root)
                || _controlsVisible && focused is Microsoft.UI.Xaml.UIElement { Visibility: Microsoft.UI.Xaml.Visibility.Visible })
            {
                return;
            }

            root.IsTabStop = true;
            root.Focus(Microsoft.UI.Xaml.FocusState.Programmatic);
        }

        private async void OnPreviewKeyDown(object sender, Microsoft.UI.Xaml.Input.KeyRoutedEventArgs e)
        {
            if (_menuOpen)
            {
                if (e.Key == global::Windows.System.VirtualKey.Escape) DropdownMenu.Dismiss(this);
                e.Handled = e.Key == global::Windows.System.VirtualKey.Escape;
                return;
            }
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
