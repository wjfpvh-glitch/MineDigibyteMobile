using Microsoft.Maui.Controls;
using Microsoft.Maui.Controls.Xaml;

namespace MineDigibyteMobile.Localization;

[ContentProperty(nameof(Key))]
public sealed class TranslateExtension : IMarkupExtension
{
    public string Key { get; set; } = string.Empty;

    public object ProvideValue(IServiceProvider serviceProvider) =>
        new Binding(
            $"[{Key}]",
            BindingMode.OneWay,
            source: LocalizationService.Instance);
}
