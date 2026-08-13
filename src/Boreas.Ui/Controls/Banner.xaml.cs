using System.Windows.Input;
using Boreas.Ui.Presentation;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace Boreas.Ui.Controls;

public sealed partial class Banner : UserControl
{
    public Banner() => InitializeComponent();

    public static readonly DependencyProperty ToneProperty = DependencyProperty.Register(
        nameof(Tone), typeof(StatusTone), typeof(Banner), new PropertyMetadata(StatusTone.Caution));

    public static readonly DependencyProperty GlyphProperty = DependencyProperty.Register(
        nameof(Glyph), typeof(string), typeof(Banner), new PropertyMetadata(string.Empty));

    public static readonly DependencyProperty TitleProperty = DependencyProperty.Register(
        nameof(Title), typeof(string), typeof(Banner), new PropertyMetadata(string.Empty));

    public static readonly DependencyProperty MessageProperty = DependencyProperty.Register(
        nameof(Message), typeof(string), typeof(Banner), new PropertyMetadata(string.Empty));

    public static readonly DependencyProperty ActionLabelProperty = DependencyProperty.Register(
        nameof(ActionLabel), typeof(string), typeof(Banner),
        new PropertyMetadata(null, OnActionLabelChanged));

    public static readonly DependencyProperty ActionCommandProperty = DependencyProperty.Register(
        nameof(ActionCommand), typeof(ICommand), typeof(Banner), new PropertyMetadata(null));

    public StatusTone Tone
    {
        get => (StatusTone)GetValue(ToneProperty);
        set => SetValue(ToneProperty, value);
    }

    public string Glyph
    {
        get => (string)GetValue(GlyphProperty);
        set => SetValue(GlyphProperty, value);
    }

    public string Title
    {
        get => (string)GetValue(TitleProperty);
        set => SetValue(TitleProperty, value);
    }

    public string Message
    {
        get => (string)GetValue(MessageProperty);
        set => SetValue(MessageProperty, value);
    }

    /// <summary>Null when the condition offers nothing worth pressing.</summary>
    public string? ActionLabel
    {
        get => (string?)GetValue(ActionLabelProperty);
        set => SetValue(ActionLabelProperty, value);
    }

    public ICommand? ActionCommand
    {
        get => (ICommand?)GetValue(ActionCommandProperty);
        set => SetValue(ActionCommandProperty, value);
    }

    public Visibility ActionVisibility =>
        string.IsNullOrEmpty(ActionLabel) ? Visibility.Collapsed : Visibility.Visible;

    private static void OnActionLabelChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        var banner = (Banner)d;
        banner.Action.Visibility = banner.ActionVisibility;
    }
}
