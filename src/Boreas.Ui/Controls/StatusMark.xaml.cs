using Boreas.Ui.Presentation;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

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

    public static readonly DependencyProperty SurfaceProperty = DependencyProperty.Register(
        nameof(Surface), typeof(MarkSurface), typeof(StatusMark),
        new PropertyMetadata(MarkSurface.Canvas, OnVisualPropertyChanged));

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
    /// Whether work is under way; reduced motion hides movement, not state.
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

    /// <summary>
    /// Surface beneath the mark; band and canvas use different tone families.
    /// </summary>
    public MarkSurface Surface
    {
        get => (MarkSurface)GetValue(SurfaceProperty);
        set => SetValue(SurfaceProperty, value);
    }

    private static void OnVisualPropertyChanged(DependencyObject d, DependencyPropertyChangedEventArgs e) =>
        ((StatusMark)d).Sync();

    private void Sync()
    {
        VisualStateManager.GoToState(this, $"{Surface}{Tone}", useTransitions: false);

        // Read at use time so a motion setting change takes effect immediately.
        var spin = IsBusy && App.MotionEnabled;
        Busy.Visibility = spin ? Visibility.Visible : Visibility.Collapsed;
        Busy.IsActive = spin;

        // Glyph and spinner share a cell; show only one.
        Mark.Visibility = spin ? Visibility.Collapsed : Visibility.Visible;
    }
}

/// <summary>The two grounds a mark can sit on, closed.</summary>
public enum MarkSurface
{
    Canvas,
    Band,
}
