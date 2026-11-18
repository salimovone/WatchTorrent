using WatchTorrent.Services;

namespace WatchTorrent.Views;

public partial class PlayerPage
{
    // Barmoq shuncha bosib turilsa — 2x tezlik (YouTube'dagi kabi).
    private const int LongPressMs = 500;

    // Yuqori/pastki chetdan boshlangan surish tizim panellarini chiqarish uchun — yorug'lik/ovozga tegmaydi.
    private const double SystemEdgeDip = 40;

    // Ekran balandligining shuncha qismi bo'ylab surish darajani 0% dan 100% gacha o'zgartiradi.
    private const double LevelSwipeHeight = 0.7;

    private enum TouchMode
    {
        None,
        Pending,
        HoldSpeed,
        Brightness,
        Volume,
        Ignored
    }

    private TouchMode _touchMode;
    private double _touchStartX;
    private double _touchStartY;
    private double _levelStartY;
    private double _levelStart;
    private string? _levelIcon;
    private CancellationTokenSource? _longPress;
    private CancellationTokenSource? _hideLevel;

    private void BeginTouch(double x, double y)
    {
        EndTouch(null);
        if (_isShutDown || PlaylistOverlay.IsVisible) return;
        _touchMode = TouchMode.Pending;
        _touchStartX = x;
        _touchStartY = y;
        _ = DetectLongPressAsync();
    }

    private void OnTouchMove(double x, double y, double width, double height, double slop = 8)
    {
        if (_touchMode == TouchMode.Pending)
        {
            var dx = x - _touchStartX;
            var dy = y - _touchStartY;

            if (Math.Abs(dx) < slop && Math.Abs(dy) < slop)
            {
                return;
            }

            _longPress?.Cancel();

            var edge = SystemEdgeDip;
            var nearEdge = _touchStartY < edge || _touchStartY > height - edge;

            if (!PlayerWindow.CanAdjustLevels || nearEdge || Math.Abs(dy) <= Math.Abs(dx))
            {
                _touchMode = TouchMode.Ignored;

                return;
            }

            _touchMode = _touchStartX < width / 2 ? TouchMode.Brightness : TouchMode.Volume;
            _levelStart = _touchMode == TouchMode.Brightness ? PlayerWindow.Brightness : PlayerWindow.Volume;
            _levelStartY = y;
            HideControls();
        }

        if (_touchMode is not (TouchMode.Brightness or TouchMode.Volume) || height <= 0)
        {
            return;
        }

        var level = Math.Clamp(_levelStart + (_levelStartY - y) / (height * LevelSwipeHeight), 0, 1);

        if (_touchMode == TouchMode.Brightness)
        {
            PlayerWindow.Brightness = level;
            ShowLevel("ic_brightness.png", level);
        }
        else
        {
            // Tizim ovozi pog'onali (odatda 15 ta) — haqiqiy o'rnatilgan qiymat ko'rsatiladi.
            PlayerWindow.Volume = level;

            var volume = PlayerWindow.Volume;
            ShowLevel(volume > 0 ? "ic_audio.png" : "ic_volume_off.png", volume);
        }
    }

    private void EndTouch(double? tapX)
    {
        _longPress?.Cancel();
        _longPress?.Dispose();
        _longPress = null;

        var mode = _touchMode;
        _touchMode = TouchMode.None;

        switch (mode)
        {
            case TouchMode.Pending when tapX is { } x:
                HandleTap(x);
                break;
            case TouchMode.HoldSpeed:
                _viewModel.EndHoldSpeed();
                HoldSpeedBadge.Opacity = 0;
                break;
            case TouchMode.Brightness or TouchMode.Volume:
                _ = HideLevelAsync();
                break;
        }
    }

    private void CancelSurfaceInput()
    {
        EndTouch(null);
        _singleTap?.Cancel();
        _lastTapAt = 0;
        _hideLevel?.Cancel();
        LevelIndicator.Opacity = 0;
    }

    private async Task DetectLongPressAsync()
    {
        _longPress?.Cancel();
        var longPress = _longPress = new CancellationTokenSource();

        try
        {
            await Task.Delay(LongPressMs, longPress.Token);
        }
        catch (TaskCanceledException)
        {
            return;
        }

        if (longPress.IsCancellationRequested || _isShutDown || _touchMode != TouchMode.Pending)
        {
            return;
        }

        // Pauza yoki yuklanish paytida bosib turish hech narsa qilmaydi (qo'yib yuborilganda bosish ham hisoblanmaydi).
        if (!_viewModel.BeginHoldSpeed())
        {
            _touchMode = TouchMode.Ignored;

            return;
        }

        _touchMode = TouchMode.HoldSpeed;
        _singleTap?.Cancel();
        _lastTapAt = 0;
        HideControls();
        HoldSpeedBadge.Opacity = 1;
#if ANDROID
        _touchView?.PerformHapticFeedback(Android.Views.FeedbackConstants.LongPress);
#elif IOS
        using var feedback = new UIKit.UIImpactFeedbackGenerator(UIKit.UIImpactFeedbackStyle.Light);
        feedback.ImpactOccurred();
#endif
    }

    private void ShowLevel(string icon, double level)
    {
        _hideLevel?.Cancel();

        // Rasm har harakatda qayta yuklanmasin.
        if (_levelIcon != icon)
        {
            _levelIcon = icon;
            LevelIcon.Source = icon;
        }

        LevelBar.Progress = level;
        LevelText.Text = $"{level * 100:0}%";
        LevelIndicator.Opacity = 1;
    }

    private async Task HideLevelAsync()
    {
        _hideLevel?.Cancel();
        var hideLevel = _hideLevel = new CancellationTokenSource();

        try
        {
            await Task.Delay(800, hideLevel.Token);
            LevelIndicator.Opacity = 0;
        }
        catch (TaskCanceledException)
        {
        }
    }

}
