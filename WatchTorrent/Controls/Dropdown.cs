using System.Collections;
using Microsoft.Maui.Controls.Shapes;

namespace WatchTorrent.Controls;

/// <summary>Theme-aware select field. Item labels remain bound so changing language updates them.</summary>
public sealed class Dropdown : ContentView
{
    public static readonly BindableProperty ItemsSourceProperty = BindableProperty.Create(nameof(ItemsSource), typeof(IList), typeof(Dropdown), propertyChanged: Changed);
    public static readonly BindableProperty SelectedItemProperty = BindableProperty.Create(nameof(SelectedItem), typeof(object), typeof(Dropdown), defaultBindingMode: BindingMode.TwoWay, propertyChanged: ItemChanged);
    public static readonly BindableProperty SelectedIndexProperty = BindableProperty.Create(nameof(SelectedIndex), typeof(int), typeof(Dropdown), -1, BindingMode.TwoWay, propertyChanged: IndexChanged);
    public static readonly BindableProperty DisplayMemberPathProperty = BindableProperty.Create(nameof(DisplayMemberPath), typeof(string), typeof(Dropdown), "Title", propertyChanged: Changed);
    public static readonly BindableProperty TitleProperty = BindableProperty.Create(nameof(Title), typeof(string), typeof(Dropdown), "", propertyChanged: Changed);
    public static readonly BindableProperty TrailingIconProperty = BindableProperty.Create(nameof(TrailingIcon), typeof(string), typeof(Dropdown), null,
        propertyChanged: (view, _, _) => ((Dropdown)view).UpdateTrailingIcon());

    public IList? ItemsSource { get => (IList?)GetValue(ItemsSourceProperty); set => SetValue(ItemsSourceProperty, value); }
    public object? SelectedItem { get => GetValue(SelectedItemProperty); set => SetValue(SelectedItemProperty, value); }
    public int SelectedIndex { get => (int)GetValue(SelectedIndexProperty); set => SetValue(SelectedIndexProperty, value); }
    public string DisplayMemberPath { get => (string)GetValue(DisplayMemberPathProperty); set => SetValue(DisplayMemberPathProperty, value); }
    public string Title { get => (string)GetValue(TitleProperty); set => SetValue(TitleProperty, value); }
    public string? TrailingIcon { get => (string?)GetValue(TrailingIconProperty); set => SetValue(TrailingIconProperty, value); }

    private readonly Label label = new() { FontSize = 14, FontFamily = "OpenSansSemibold", VerticalOptions = LayoutOptions.Center, LineBreakMode = LineBreakMode.TailTruncation, MaxLines = 1 };
    private readonly Image arrow = new() { Source = "ic_chevron_down.png", WidthRequest = 20, HeightRequest = 20, Aspect = Aspect.AspectFit, VerticalOptions = LayoutOptions.Center, HorizontalOptions = LayoutOptions.End };
    private readonly Image trailingImage = new() { WidthRequest = 20, HeightRequest = 14, Aspect = Aspect.AspectFit, VerticalOptions = LayoutOptions.Center, IsVisible = false };
    private bool syncing;
    private bool open;

    public Dropdown()
    {
        MinimumHeightRequest = 48;
        var grid = new Grid { ColumnDefinitions = { new ColumnDefinition(GridLength.Star), new ColumnDefinition(new GridLength(20)) }, ColumnSpacing = 8, InputTransparent = true };
        grid.Children.Add(label);
        Grid.SetColumn(arrow, 1);
        grid.Children.Add(arrow);
        Grid.SetColumn(trailingImage, 1);
        grid.Children.Add(trailingImage);
        var field = new Border { Content = grid, Padding = new Thickness(14, 10), StrokeThickness = 1, StrokeShape = new RoundRectangle { CornerRadius = 10 } };
        field.SetDynamicResource(Border.BackgroundColorProperty, "Surface");
        field.Stroke = new SolidColorBrush((Color)Application.Current!.Resources["StrokeColor"]);
        label.SetDynamicResource(Label.TextColorProperty, "TextPrimary");
        // Transparent button supplies keyboard activation and a native accessibility role.
        var hit = new Button { BackgroundColor = Colors.Transparent, Text = "", BorderWidth = 0, Padding = 0, MinimumHeightRequest = 48 };
        hit.Clicked += async (_, _) =>
        {
            if (open || ItemsSource == null || ItemsSource.Count == 0) return;
            open = true;
            try
            {
                var names = ItemsSource.Cast<object>().Select(TextOf).ToArray();
                var index = await DropdownMenu.ShowAsync(this, names, SelectedIndex);
                if (index >= 0 && index < ItemsSource.Count) SelectedIndex = index;
            }
            finally { open = false; }
        };
        var host = new Grid();
        host.Children.Add(field);
        host.Children.Add(hit);
        Content = host;
        label.PropertyChanged += (_, e) => { if (e.PropertyName == nameof(Label.Text)) SemanticProperties.SetDescription(hit, $"{Title}: {label.Text}"); };
    }

    private void UpdateTrailingIcon()
    {
        bool hasIcon = !string.IsNullOrWhiteSpace(TrailingIcon);
        trailingImage.Source = hasIcon ? TrailingIcon : null;
        trailingImage.IsVisible = hasIcon;
        arrow.IsVisible = !hasIcon;
    }

    private string TextOf(object item)
    {
        var text = new Label();
        text.SetBinding(Label.TextProperty, new Binding(DisplayMemberPath, source: item));
        var result = text.Text ?? item.ToString() ?? "";
        text.RemoveBinding(Label.TextProperty);
        return result;
    }

    private void Refresh()
    {
        label.RemoveBinding(Label.TextProperty);
        if (SelectedItem != null) label.SetBinding(Label.TextProperty, new Binding(DisplayMemberPath, source: SelectedItem));
        else label.Text = Title;
    }
    private static void Changed(BindableObject bindable, object oldValue, object newValue)
    {
        var control = (Dropdown)bindable;
        if (control.ItemsSource is { } items && control.SelectedIndex >= 0 && control.SelectedIndex < items.Count)
            control.SelectedItem = items[control.SelectedIndex];
        control.Refresh();
    }
    private static void ItemChanged(BindableObject bindable, object oldValue, object newValue)
    {
        var control = (Dropdown)bindable;
        if (!control.syncing)
        {
            control.syncing = true;
            control.SelectedIndex = control.ItemsSource?.IndexOf(newValue) ?? -1;
            control.syncing = false;
        }
        control.Refresh();
    }
    private static void IndexChanged(BindableObject bindable, object oldValue, object newValue)
    {
        var control = (Dropdown)bindable;
        if (control.syncing) return;
        control.syncing = true;
        var index = (int)newValue;
        control.SelectedItem = control.ItemsSource is { } items && index >= 0 && index < items.Count ? items[index] : null;
        control.syncing = false;
        control.Refresh();
    }
}
