using Microsoft.Maui.Controls.Shapes;

namespace WatchTorrent.Controls;

/// <summary>A small, anchored menu in the page's visual tree; never opens a native dialog.</summary>
public static class DropdownMenu
{
    private static readonly Dictionary<ContentPage, Action> OpenMenus = new();

    public static bool Dismiss(ContentPage page)
    {
        if (!OpenMenus.TryGetValue(page, out var close)) return false;
        close();
        return true;
    }

    public static Task<int> ShowAsync(View anchor, IReadOnlyList<string> options, int selected = -1)
    {
        var page = FindPage(anchor);
        if (page?.Content is not Grid root || options.Count == 0) return Task.FromResult(-1);
        Dismiss(page);

        var completion = new TaskCompletionSource<int>();
        var overlay = new AbsoluteLayout { ZIndex = 1000, BackgroundColor = Colors.Transparent };
        Grid.SetRowSpan(overlay, Math.Max(1, root.RowDefinitions.Count));
        Grid.SetColumnSpan(overlay, Math.Max(1, root.ColumnDefinitions.Count));
        var outside = new BoxView { Color = Colors.Transparent, BackgroundColor = Colors.Transparent };
        AbsoluteLayout.SetLayoutBounds(outside, new Rect(0, 0, 1, 1));
        AbsoluteLayout.SetLayoutFlags(outside, Microsoft.Maui.Layouts.AbsoluteLayoutFlags.All);
        overlay.Children.Add(outside);

        void Close(int result)
        {
            if (!OpenMenus.Remove(page)) return;
#if WINDOWS
            if (root.Handler?.PlatformView is Microsoft.UI.Xaml.UIElement nativeRoot) nativeRoot.PreviewKeyDown -= OnKeyDown;
#endif
            root.SizeChanged -= OnLayoutChanged;
            page.Disappearing -= OnLayoutChanged;
            root.Children.Remove(overlay);
            completion.TrySetResult(result);
            anchor.Focus();
        }
        void OnLayoutChanged(object? sender, EventArgs e) => Close(-1);
#if WINDOWS
        void OnKeyDown(object sender, Microsoft.UI.Xaml.Input.KeyRoutedEventArgs e)
        {
            if (e.Key != global::Windows.System.VirtualKey.Escape) return;
            Close(-1);
            e.Handled = true;
        }
        if (root.Handler?.PlatformView is Microsoft.UI.Xaml.UIElement nativeRoot) nativeRoot.PreviewKeyDown += OnKeyDown;
#endif
        OpenMenus[page] = () => Close(-1);
        root.SizeChanged += OnLayoutChanged;
        page.Disappearing += OnLayoutChanged;
        var dismiss = new TapGestureRecognizer();
        dismiss.Tapped += (_, _) => Close(-1);
        outside.GestureRecognizers.Add(dismiss);

        var rows = new VerticalStackLayout { Spacing = 2, Padding = 6 };
        for (var i = 0; i < options.Count; i++)
        {
            var index = i;
            var current = i == selected;
            var button = new Button
            {
                Text = current ? $"✓  {options[i]}" : options[i],
                TextColor = Theme(current ? "Accent" : "TextPrimary"),
                BackgroundColor = current ? Theme("AccentSoft") : Colors.Transparent,
                FontFamily = current ? "OpenSansSemibold" : "OpenSansRegular",
                FontSize = 14,
                CornerRadius = 8,
                Padding = new Thickness(12, 8),
                MinimumHeightRequest = 48,
                HorizontalOptions = LayoutOptions.Fill,
                BorderWidth = 0,
                AutomationId = $"DropdownOption{i}"
            };
            button.Clicked += (_, _) => Close(index);
            rows.Children.Add(button);
        }

        var menu = new Border
        {
            BackgroundColor = Theme("Surface"),
            Stroke = new SolidColorBrush(Theme("StrokeColor")),
            StrokeThickness = 1,
            StrokeShape = new RoundRectangle { CornerRadius = 12 },
            Padding = 0,
            Shadow = new Shadow { Brush = Brush.Black, Opacity = 0.25f, Radius = 16, Offset = new Point(0, 6) },
            Content = new ScrollView { Content = rows, VerticalScrollBarVisibility = ScrollBarVisibility.Never }
        };
        var point = Position(anchor, root);
        var width = Math.Min(Math.Max(anchor.Width, 260), Math.Max(1, root.Width - 32));
        var x = Math.Clamp(point.X + anchor.Width - width, 16, Math.Max(16, root.Width - width - 16));
        var below = root.Height - point.Y - anchor.Height - 12;
        var above = point.Y - 12;
        var openBelow = below >= Math.Min(options.Count * 50 + 12, 280) || below >= above;
        var height = Math.Min(Math.Min(options.Count * 50 + 12, 340), Math.Max(48, openBelow ? below : above));
        var y = openBelow ? point.Y + anchor.Height + 6 : point.Y - height - 6;
        AbsoluteLayout.SetLayoutBounds(menu, new Rect(x, Math.Max(8, y), width, height));
        overlay.Children.Add(menu);
        root.Children.Add(overlay);
        menu.Loaded += (_, _) => ((Button)rows.Children[Math.Clamp(selected, 0, options.Count - 1)]).Focus();
        return completion.Task;
    }

    private static Color Theme(string key) => (Color)Application.Current!.Resources[key];

    private static ContentPage? FindPage(Element element)
    {
        for (Element? current = element; current != null; current = current.Parent)
            if (current is ContentPage page) return page;
        return null;
    }

    private static Point Position(View anchor, Grid root)
    {
#if ANDROID
        if (anchor.Handler?.PlatformView is Android.Views.View nativeAnchor && root.Handler?.PlatformView is Android.Views.View nativeRoot)
        {
            var a = new int[2];
            var r = new int[2];
            nativeAnchor.GetLocationInWindow(a);
            nativeRoot.GetLocationInWindow(r);
            var density = nativeAnchor.Resources!.DisplayMetrics!.Density;
            return new Point((a[0] - r[0]) / density, (a[1] - r[1]) / density);
        }
#elif WINDOWS
        if (anchor.Handler?.PlatformView is Microsoft.UI.Xaml.UIElement nativeAnchor && root.Handler?.PlatformView is Microsoft.UI.Xaml.UIElement nativeRoot)
        {
            var point = nativeAnchor.TransformToVisual(nativeRoot).TransformPoint(new Windows.Foundation.Point());
            return new Point(point.X, point.Y);
        }
#elif IOS
        if (anchor.Handler?.PlatformView is UIKit.UIView nativeAnchor && root.Handler?.PlatformView is UIKit.UIView nativeRoot)
        {
            var point = nativeAnchor.ConvertPointToView(CoreGraphics.CGPoint.Empty, nativeRoot);
            return new Point(point.X, point.Y);
        }
#endif
        var position = new Point();
        for (Element? element = anchor; element != null && element != root; element = element.Parent)
            if (element is VisualElement view) position = new Point(position.X + view.X, position.Y + view.Y);
        return position;
    }
}
