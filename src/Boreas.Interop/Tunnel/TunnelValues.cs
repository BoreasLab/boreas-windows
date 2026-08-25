using System.Globalization;
using System.Net;

namespace Boreas.Interop.Tunnel;

/// <summary>The largest packet the tunnel carries, in bytes.</summary>
/// <remarks>
/// <b>One value, used twice.</b> This same number becomes
/// <c>BoreasDevice.mtu</c> and <c>BoreasConfig.mtu</c>, which is what removes
/// the second of the two silent mistakes: telling the two sides different
/// numbers produces a tunnel that starts, reports itself healthy, and spends
/// its time answering Packet Too Big to senders that never converge, with a
/// sustained <c>paths_reported</c> as the only symptom. Two fields that must
/// agree are a bug waiting; one field cannot disagree with itself.
/// </remarks>
public sealed record Mtu
{
    /// <summary>The IPv6 floor. Below this, start fails with BOREAS_CONFIG.</summary>
    public const int Minimum = 1280;

    /// <summary>The field crosses as <c>uint16_t</c>.</summary>
    public const int Maximum = ushort.MaxValue;

    public static readonly string Requirement =
        $"The MTU must be a number from {Minimum} to {Maximum}.";

    private Mtu(ushort value) => Value = value;

    public ushort Value { get; }

    public static Mtu? TryCreate(int value) =>
        value is >= Minimum and <= Maximum ? new Mtu((ushort)value) : null;

    public override string ToString() => Value.ToString(CultureInfo.InvariantCulture);
}

/// <summary>
/// A <c>"host:port"</c> with a numeric address, as the ABI requires for a
/// resolver and for a WireGuard endpoint.
/// </summary>
/// <remarks>
/// Numeric, never a name: a name here would have to be resolved, and the only
/// resolver available at that moment is the one being configured.
/// </remarks>
public sealed record HostPort
{
    public const string Requirement =
        "Write this as a numeric address and a port, for example 1.1.1.1:53 or [fd00::1]:53.";

    private HostPort(IPEndPoint value) => Value = value;

    public IPEndPoint Value { get; }

    public static HostPort? TryParse(string? raw) =>
        IPEndPoint.TryParse(raw?.Trim() ?? string.Empty, out var endpoint) && endpoint.Port != 0
            ? new HostPort(endpoint)
            : null;

    /// <summary>
    /// The one spelling the ABI is ever sent. IPv6 keeps its brackets, which is
    /// what makes the port unambiguous.
    /// </summary>
    public override string ToString() => Value.ToString();
}

/// <remarks>
/// <b>An allowlist entry, never a pattern.</b> Interception forges a
/// certificate, and the set of hosts that happens to should be one a person can
/// read. An address is refused as well as a pattern: interception is selected
/// by the name a DNS answer carried, so an address here names nothing.
/// </remarks>
public sealed record Hostname
{
    public const string Requirement =
        "Enter a host name, for example news.example.com. Addresses and wildcards are not host names.";

    /// <summary>The longest a DNS name may be, in bytes.</summary>
    private const int MaximumLength = 253;

    private const int MaximumLabelLength = 63;

    private Hostname(string value) => Value = value;

    public string Value { get; }

    public static Hostname? TryParse(string? raw)
    {
        if (raw?.Trim() is not { Length: > 0 and <= MaximumLength } text)
        {
            return null;
        }

        // An address is well-formed text that is not a name. Checked first
        // because "1.2.3.4" passes every label rule below.
        if (IPAddress.TryParse(text, out _))
        {
            return null;
        }

        // Validate each label after splitting once. The checks are linear in
        // the input length; Split allocates the label substrings.
        foreach (var label in text.Split('.'))
        {
            if (label.Length is 0 or > MaximumLabelLength
                || label[0] == '-'
                || label[^1] == '-'
                || !label.All(static c => char.IsAsciiLetterOrDigit(c) || c == '-'))
            {
                return null;
            }
        }

        return new Hostname(text);
    }

    public override string ToString() => Value;
}

/// <summary>Thirty-two raw key bytes.</summary>
/// <remarks>
/// Raw, never the base64 a WireGuard configuration file carries.
/// <see cref="TryFromBase64"/> is the decoder, so a caller holding a
/// configuration file does not have to know that.
/// </remarks>
public sealed record Key32
{
    public const int Length = 32;

    public static readonly string Requirement =
        $"A key is {Length} bytes, written as {((Length + 2) / 3) * 4} base64 characters.";

    private Key32(byte[] value) => _value = value;

    private readonly byte[] _value;

    public ReadOnlySpan<byte> Value => _value;

    public static Key32? TryFrom(ReadOnlySpan<byte> bytes) =>
        bytes.Length == Length ? new Key32(bytes.ToArray()) : null;

    public static Key32? TryFromBase64(string? raw)
    {
        Span<byte> decoded = stackalloc byte[Length];

        return Convert.TryFromBase64String(raw ?? string.Empty, decoded, out var written)
            ? TryFrom(decoded[..written])
            : null;
    }

    /// <summary>
    /// Structural, because the synthesized version would compare the array by
    /// reference and report two copies of one key as different keys.
    /// </summary>
    public bool Equals(Key32? other) =>
        other is not null && _value.AsSpan().SequenceEqual(other._value);

    public override int GetHashCode()
    {
        var hash = new HashCode();
        hash.AddBytes(_value);
        return hash.ToHashCode();
    }

    /// <summary>
    /// Deliberately not the key. A private key rendered into a log, a window
    /// title, or an exception message is a private key that has left the
    /// process.
    /// </summary>
    public override string ToString() => $"<{Length}-byte key>";
}
