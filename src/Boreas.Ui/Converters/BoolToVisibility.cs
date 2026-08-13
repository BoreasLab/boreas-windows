using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Data;

namespace Boreas.Ui.Converters;

/// <summary>
/// The application's one visibility converter.
/// </summary>
/// <remarks>
/// One mechanism, used everywhere, so no reader has to work out whether a
/// given binding hides its target through a converter, a code-behind method
/// or a view-model property that returns a UI type.
/// </remarks>
public sealed class BoolToVisibility : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, string language) =>
        value is true ? Visibility.Visible : Visibility.Collapsed;

    public object ConvertBack(object value, Type targetType, object parameter, string language) =>
        value is Visibility.Visible;
}
