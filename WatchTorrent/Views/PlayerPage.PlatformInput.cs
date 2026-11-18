namespace WatchTorrent.Views;

public partial class PlayerPage
{
#if ANDROID
    private Android.Views.View? _touchView;

    private void AttachTouch()
    {
        if (_isShutDown || TouchLayer.Handler?.PlatformView is not Android.Views.View view || view == _touchView) return;
        DetachTouch();
        _touchView = view;
        view.Touch += OnTouch;
    }

    private void DetachTouch()
    {
        CancelSurfaceInput();
        if (_touchView != null) _touchView.Touch -= OnTouch;
        _touchView = null;
    }

    private void OnTouch(object? sender, Android.Views.View.TouchEventArgs e)
    {
        e.Handled = true;
        if (e.Event is not { } motion || _touchView is not { } view) return;
        var density = view.Resources?.DisplayMetrics?.Density ?? 1;
        var x = motion.GetX() / density;
        var y = motion.GetY() / density;
        switch (motion.ActionMasked)
        {
            case Android.Views.MotionEventActions.Down:
                BeginTouch(x, y);
                break;
            case Android.Views.MotionEventActions.Move:
                var slop = view.Context is { } context ? Android.Views.ViewConfiguration.Get(context)?.ScaledTouchSlop ?? 16 : 16;
                OnTouchMove(x, y, view.Width / density, view.Height / density, slop / density);
                break;
            case Android.Views.MotionEventActions.PointerDown:
                EndTouch(null);
                break;
            case Android.Views.MotionEventActions.Up:
                EndTouch(x);
                break;
            case Android.Views.MotionEventActions.Cancel:
                EndTouch(null);
                break;
        }
    }
#elif IOS
    private UIKit.UIView? _touchView;
    private UIKit.UILongPressGestureRecognizer? _surfacePress;

    private void AttachTouch()
    {
        if (_isShutDown || TouchLayer.Handler?.PlatformView is not UIKit.UIView view || view == _touchView) return;
        DetachTouch();
        _touchView = view;
        // A zero-duration recognizer supplies down/move/up for taps, holds and swipes,
        // including mouse presses when the iPad app runs on an Apple silicon Mac.
        _surfacePress = new UIKit.UILongPressGestureRecognizer(OnSurfacePress)
        {
            MinimumPressDuration = 0,
            AllowableMovement = 100000,
            NumberOfTouchesRequired = 1,
            CancelsTouchesInView = true
        };
        view.UserInteractionEnabled = true;
        view.AddGestureRecognizer(_surfacePress);
    }

    private void DetachTouch()
    {
        CancelSurfaceInput();
        if (_surfacePress != null)
        {
            _touchView?.RemoveGestureRecognizer(_surfacePress);
            _surfacePress.Dispose();
        }
        _surfacePress = null;
        _touchView = null;
    }

    private void OnSurfacePress(UIKit.UILongPressGestureRecognizer press)
    {
        if (_touchView is not { } view) return;
        if (press.NumberOfTouches > 1) { EndTouch(null); return; }
        var point = press.LocationInView(view);
        switch (press.State)
        {
            case UIKit.UIGestureRecognizerState.Began:
                BeginTouch(point.X, point.Y);
                break;
            case UIKit.UIGestureRecognizerState.Changed:
                OnTouchMove(point.X, point.Y, view.Bounds.Width, view.Bounds.Height);
                break;
            case UIKit.UIGestureRecognizerState.Ended:
                EndTouch(point.X);
                break;
            case UIKit.UIGestureRecognizerState.Cancelled:
            case UIKit.UIGestureRecognizerState.Failed:
                EndTouch(null);
                break;
        }
    }
#elif WINDOWS
    private Microsoft.UI.Xaml.UIElement? _touchView;
    private Microsoft.UI.Xaml.Input.Pointer? _surfacePointer;

    private void AttachTouch()
    {
        if (_isShutDown || TouchLayer.Handler?.PlatformView is not Microsoft.UI.Xaml.UIElement view || view == _touchView) return;
        DetachTouch();
        _touchView = view;
        view.PointerPressed += OnSurfacePointerPressed;
        view.PointerMoved += OnSurfacePointerMoved;
        view.PointerReleased += OnSurfacePointerReleased;
        view.PointerCanceled += OnSurfacePointerCancelled;
        view.PointerCaptureLost += OnSurfacePointerCancelled;
    }

    private void DetachTouch()
    {
        CancelSurfaceInput();
        if (_touchView is { } view)
        {
            view.PointerPressed -= OnSurfacePointerPressed;
            view.PointerMoved -= OnSurfacePointerMoved;
            view.PointerReleased -= OnSurfacePointerReleased;
            view.PointerCanceled -= OnSurfacePointerCancelled;
            view.PointerCaptureLost -= OnSurfacePointerCancelled;
            if (_surfacePointer != null) view.ReleasePointerCapture(_surfacePointer);
        }
        _surfacePointer = null;
        _touchView = null;
    }

    private void OnSurfacePointerPressed(object sender, Microsoft.UI.Xaml.Input.PointerRoutedEventArgs e)
    {
        var point = e.GetCurrentPoint(_touchView);
        if (!point.Properties.IsLeftButtonPressed) return;
        if (_surfacePointer != null) { EndTouch(null); return; }
        _surfacePointer = e.Pointer;
        if (_touchView?.CapturePointer(e.Pointer) != true) { _surfacePointer = null; return; }
        BeginTouch(point.Position.X, point.Position.Y);
        e.Handled = true;
    }

    private void OnSurfacePointerMoved(object sender, Microsoft.UI.Xaml.Input.PointerRoutedEventArgs e)
    {
        if (_surfacePointer?.PointerId != e.Pointer.PointerId) return;
        var point = e.GetCurrentPoint(_touchView).Position;
        OnTouchMove(point.X, point.Y, TouchLayer.Width, TouchLayer.Height);
        e.Handled = true;
    }

    private void OnSurfacePointerReleased(object sender, Microsoft.UI.Xaml.Input.PointerRoutedEventArgs e)
    {
        if (_surfacePointer?.PointerId != e.Pointer.PointerId) return;
        EndTouch(e.GetCurrentPoint(_touchView).Position.X);
        _surfacePointer = null;
        _touchView?.ReleasePointerCapture(e.Pointer);
        e.Handled = true;
    }

    private void OnSurfacePointerCancelled(object sender, Microsoft.UI.Xaml.Input.PointerRoutedEventArgs e)
    {
        if (_surfacePointer?.PointerId != e.Pointer.PointerId) return;
        EndTouch(null);
        _surfacePointer = null;
        _touchView?.ReleasePointerCapture(e.Pointer);
    }
#else
    private void AttachTouch() { }
    private void DetachTouch() => CancelSurfaceInput();
#endif
}
