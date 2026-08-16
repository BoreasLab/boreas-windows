using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Data;

namespace Boreas.Ui.Converters;

/// <summary>
/// The application's one visibility converter.
/// </summary>
/// <remarks>
/// Bindings use this converter instead of view-model properties returning UI
/// types.
/// </remarks>
public sealed class BoolToVisibility : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, string language) =>
        value is true ? Visibility.Visible : Visibility.Collapsed;

    public object ConvertBack(object value, Type targetType, object parameter, string language) =>
        value is Visibility.Visible;
}
