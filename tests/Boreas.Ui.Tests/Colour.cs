namespace Boreas.Ui.Tests;

/// <summary>
/// An sRGB colour parsed from design-token hex.
/// </summary>
/// <remarks>
/// The refined type prevents malformed tokens from reaching contrast math.
/// </remarks>
public readonly record struct Colour(byte R, byte G, byte B)
{
    /// <summary>
    /// Parses literal six- or eight-digit hex colours; returns null otherwise.
    /// </summary>
    public static Colour? TryParse(string? raw)
    {
        if (raw is null)
        {
            return null;
        }

        var text = raw.Trim();
        if (text.Length == 0 || text[0] != '#')
        {
            return null;
        }

        var digits = text[1..];

        // Pairings are opaque; discard #aarrggbb's alpha until compositing exists.
        if (digits.Length == 8)
        {
            digits = digits[2..];
        }

        if (digits.Length != 6 || !digits.All(Uri.IsHexDigit))
        {
            return null;
        }

        return new Colour(
            Convert.ToByte(digits[..2], 16),
            Convert.ToByte(digits[2..4], 16),
            Convert.ToByte(digits[4..], 16));
    }

    /// <summary>WCAG 2.x relative luminance.</summary>
    public double Luminance =>
        (0.2126 * Linear(R)) + (0.7152 * Linear(G)) + (0.0722 * Linear(B));

    private static double Linear(byte channel)
    {
        var c = channel / 255.0;
        return c <= 0.04045 ? c / 12.92 : Math.Pow((c + 0.055) / 1.055, 2.4);
    }

    /// <summary>
    /// Contrast ratio. Symmetric, in [1, 21].
    /// </summary>
    public static double Contrast(Colour a, Colour b)
    {
        var (hi, lo) = a.Luminance >= b.Luminance
            ? (a.Luminance, b.Luminance)
            : (b.Luminance, a.Luminance);
        return (hi + 0.05) / (lo + 0.05);
    }

    /// <summary>
    /// CIE 1976 distance, used where contrast cannot show perceptual difference.
    /// </summary>
    public static double Distance(Colour a, Colour b)
    {
        var (l1, a1, b1) = a.ToLab();
        var (l2, a2, b2) = b.ToLab();
        return Math.Sqrt(((l1 - l2) * (l1 - l2)) + ((a1 - a2) * (a1 - a2)) + ((b1 - b2) * (b1 - b2)));
    }

    private (double L, double A, double B) ToLab()
    {
        double x = (0.4124 * Linear(R)) + (0.3576 * Linear(G)) + (0.1805 * Linear(B));
        double y = Luminance;
        double z = (0.0193 * Linear(R)) + (0.1192 * Linear(G)) + (0.9505 * Linear(B));

        var fx = F(x / 0.95047);
        var fy = F(y);
        var fz = F(z / 1.08883);

        return ((116 * fy) - 16, 500 * (fx - fy), 200 * (fy - fz));

        static double F(double t) =>
            t > 216.0 / 24389.0 ? Math.Cbrt(t) : ((841.0 / 108.0) * t) + (4.0 / 29.0);
    }
}
