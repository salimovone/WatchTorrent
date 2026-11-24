using WatchTorrent.Core.Localization;
using Microsoft.Maui.Controls.Xaml;

namespace WatchTorrent.Localization;

[ContentProperty(nameof(Key))]
[AcceptEmptyServiceProvider]
public sealed class TranslateExtension : IMarkupExtension<BindingBase>
{
    public string Key { get; set; } = "";
    public BindingBase ProvideValue(IServiceProvider serviceProvider) => new Binding($"[{Key}]", BindingMode.OneWay, source: AppText.Instance);
    object IMarkupExtension.ProvideValue(IServiceProvider serviceProvider) => ProvideValue(serviceProvider);
}
