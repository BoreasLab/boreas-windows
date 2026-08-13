using Boreas.Ui.Presentation;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;

namespace Boreas.Ui.Controls;

public sealed partial class StatusMark : UserControl
{
    public StatusMark()
    {
        InitializeComponent();
        Loaded += (_, _) => Sync();
    }

    public static readonly DependencyProperty ToneProperty = DependencyProperty.Register(
        nameof(Tone), typeof(StatusTone), typeof(StatusMark),
        new PropertyMetadata(StatusTone.Idle, OnVisualPropertyChanged));

    public static readonly DependencyProperty GlyphProperty = DependencyProperty.Register(
        nameof(Glyph), typeof(string), typeof(StatusMark), new PropertyMetadata(string.Empty));

    public static readonly DependencyProperty IsBusyProperty = DependencyProperty.Register(
        nameof(IsBusy), typeof(bool), typeof(StatusMark),
        new PropertyMetadata(false, OnVisualPropertyChanged));

    public static readonly DependencyProperty DiameterProperty = DependencyProperty.Register(
        nameof(Diameter), typeof(double), typeof(StatusMark), new PropertyMetadata(10d));

    public static readonly DependencyProperty GlyphSizeProperty = DependencyProperty.Register(
        nameof(GlyphSize), typeof(double), typeof(StatusMark), new PropertyMetadata(0d));

    public static readonly DependencyProperty RingThicknessProperty = DependencyProperty.Register(
        nameof(RingThickness), typeof(double), typeof(StatusMark), new PropertyMetadata(0d));

    public static readonly DependencyProperty FillBrushProperty = DependencyProperty.Register(
        nameof(FillBrush), typeof(Brush), typeof(StatusMark), new PropertyMetadata(null));

    /// <summary>The single axis of identity.</summary>
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

    /// <summary>
    /// Whether work is under way. Honoured only when the system allows
    /// animation; the state itself is always readable from the words beside
    /// the mark, so switching the spinner off removes motion, not meaning.
    /// </summary>
    public bool IsBusy
    {
        get => (bool)GetValue(IsBusyProperty);
        set => SetValue(IsBusyProperty, value);
    }

    public double Diameter
    {
        get => (double)GetValue(DiameterProperty);
        set => SetValue(DiameterProperty, value);
    }

    public double GlyphSize
    {
        get => (double)GetValue(GlyphSizeProperty);
        set => SetValue(GlyphSizeProperty, value);
    }

    public double RingThickness
    {
        get => (double)GetValue(RingThicknessProperty);
        set => SetValue(RingThicknessProperty, value);
    }

    /// <summary>Solid for a dot, unset for a ring.</summary>
    public Brush? FillBrush
    {
        get => (Brush?)GetValue(FillBrushProperty);
        set => SetValue(FillBrushProperty, value);
    }

    private static void OnVisualPropertyChanged(DependencyObject d, DependencyPropertyChangedEventArgs e) =>
        ((StatusMark)d).Sync();

    private void Sync()
    {
        VisualStateManager.GoToState(this, Tone.ToString(), useTransitions: false);

        // Checked at the point of use, so turning animation off mid-session
        // takes effect on the next state change rather than the next launch.
        var spin = IsBusy && App.MotionEnabled;
        Busy.Visibility = spin ? Visibility.Visible : Visibility.Collapsed;
        Busy.IsActive = spin;

        // The glyph and the spinner occupy the same cell, so only one shows.
        Mark.Visibility = spin ? Visibility.Collapsed : Visibility.Visible;
    }
}
