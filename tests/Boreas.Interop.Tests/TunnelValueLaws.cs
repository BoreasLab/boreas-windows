using Boreas.Interop.Tunnel;

namespace Boreas.Interop.Tests;

/// <summary>
/// Laws for the refined values, which are what turn four of the ten
/// BOREAS_CONFIG causes into a sentence naming the field.
/// </summary>
public sealed class TunnelValueLaws
{
    [Theory]
    [InlineData(Mtu.Minimum)]
    [InlineData(1420)]
    [InlineData(Mtu.Maximum)]
    public void An_mtu_within_the_range_is_accepted(int value) =>
        Assert.Equal(value, Present.Value(Mtu.TryCreate(value)).Value);

    [Theory]
    // 1280 is the IPv6 floor, and start refuses anything below it.
    [InlineData(Mtu.Minimum - 1)]
    [InlineData(576)]
    [InlineData(0)]
    [InlineData(-1)]
    // The field crosses as uint16_t, so this would wrap rather than be refused.
    [InlineData(Mtu.Maximum + 1)]
    [InlineData(70000)]
    public void An_mtu_outside_the_range_is_refused(int value) =>
        Assert.Null(Mtu.TryCreate(value));

    [Theory]
    [InlineData("1.1.1.1:53", "1.1.1.1:53")]
    [InlineData("  9.9.9.9:53  ", "9.9.9.9:53")]
    // IPv6 keeps its brackets, which is what makes the port unambiguous.
    [InlineData("[fd00::1]:53", "[fd00::1]:53")]
    [InlineData("[::1]:5353", "[::1]:5353")]
    public void A_numeric_endpoint_round_trips_to_one_spelling(string typed, string canonical) =>
        Assert.Equal(canonical, Present.Value(HostPort.TryParse(typed)).ToString());

    [Theory]
    // A name, not an address: the only resolver available to resolve it would
    // be the one being configured.
    [InlineData("dns.quad9.net:53")]
    [InlineData("localhost:53")]
    // No port.
    [InlineData("1.1.1.1")]
    [InlineData("fd00::1")]
    // Port zero is not a port anything listens on.
    [InlineData("1.1.1.1:0")]
    // Unbracketed IPv6 makes the last group and the port indistinguishable.
    [InlineData("fd00::1:53")]
    [InlineData("")]
    [InlineData(null)]
    public void Anything_that_is_not_a_numeric_endpoint_is_refused(string? typed) =>
        Assert.Null(HostPort.TryParse(typed));

    [Theory]
    [InlineData("news.example.com")]
    [InlineData("example.com")]
    [InlineData("a.b.c.d.example")]
    [InlineData("xn--80ak6aa92e.com")]
    [InlineData("host-1.example")]
    public void A_host_name_is_accepted(string typed) =>
        Assert.Equal(typed, Present.Value(Hostname.TryParse(typed)).Value);

    [Theory]
    // An allowlist, never a pattern. Interception forges a certificate, so the
    // set it applies to has to be one a person can read.
    [InlineData("*.example.com")]
    [InlineData("*")]
    // An address names nothing: interception is selected by the name a DNS
    // answer carried.
    [InlineData("10.7.0.1")]
    [InlineData("fd00::1")]
    // Malformed labels.
    [InlineData("-example.com")]
    [InlineData("example-.com")]
    [InlineData("exa mple.com")]
    [InlineData("example..com")]
    [InlineData(".example.com")]
    [InlineData("example.com.")]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    public void Anything_that_is_not_a_host_name_is_refused(string? typed) =>
        Assert.Null(Hostname.TryParse(typed));

    [Fact]
    public void A_name_longer_than_dns_allows_is_refused()
    {
        var label = new string('a', 63);
        var tooLong = string.Join('.', label, label, label, label);

        Assert.True(tooLong.Length > 253);
        Assert.Null(Hostname.TryParse(tooLong));
    }

    [Fact]
    public void A_label_longer_than_dns_allows_is_refused() =>
        Assert.Null(Hostname.TryParse(new string('a', 64) + ".example"));

    [Fact]
    public void A_key_is_thirty_two_bytes_and_nothing_else()
    {
        Assert.NotNull(Key32.TryFrom(new byte[32]));
        Assert.Null(Key32.TryFrom(new byte[31]));
        Assert.Null(Key32.TryFrom(new byte[33]));
        Assert.Null(Key32.TryFrom([]));
    }

    /// <summary>
    /// The keys are raw, not the base64 a WireGuard configuration file carries,
    /// so the decoder is here rather than in whatever reads the file.
    /// </summary>
    [Fact]
    public void A_key_decodes_from_the_base64_a_wireguard_file_carries()
    {
        var bytes = Enumerable.Range(0, 32).Select(static i => (byte)i).ToArray();

        var decoded = Present.Value(Key32.TryFromBase64(Convert.ToBase64String(bytes)));

        Assert.True(decoded.Value.SequenceEqual(bytes));
        Assert.Equal(Present.Value(Key32.TryFrom(bytes)), decoded);
    }

    [Theory]
    [InlineData("not base64 at all")]
    [InlineData("c2hvcnQ=")]
    [InlineData("")]
    [InlineData(null)]
    public void Anything_that_is_not_a_thirty_two_byte_key_is_refused(string? typed) =>
        Assert.Null(Key32.TryFromBase64(typed));

    /// <summary>
    /// A private key rendered into a log, a window title, or an exception
    /// message is a private key that has left the process.
    /// </summary>
    [Fact]
    public void A_key_never_renders_its_bytes()
    {
        var bytes = Enumerable.Range(0, 32).Select(static i => (byte)0xAB).ToArray();
        var key = Present.Value(Key32.TryFrom(bytes));

        Assert.DoesNotContain("ab", key.ToString(), StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(Convert.ToBase64String(bytes), key.ToString(), StringComparison.Ordinal);
    }

    /// <summary>
    /// Two copies of one key are one key. The synthesized record equality
    /// compares the array by reference and would call them different.
    /// </summary>
    [Fact]
    public void Two_copies_of_a_key_are_equal()
    {
        var bytes = new byte[32];
        var left = Present.Value(Key32.TryFrom(bytes));
        var right = Present.Value(Key32.TryFrom(bytes.ToArray()));

        Assert.Equal(left, right);
        Assert.Equal(left.GetHashCode(), right.GetHashCode());
    }

    /// <summary>
    /// Forging certificates for the empty set is the name tier with extra
    /// machinery, and start refuses it. A refined collection is what stops it
    /// being writable.
    /// </summary>
    [Fact]
    public void Interception_needs_at_least_one_host()
    {
        Assert.Null(InterceptHosts.TryCreate([]));
        Assert.NotNull(InterceptHosts.TryCreate([Present.Value(Hostname.TryParse("news.example.com"))]));
    }

    /// <summary>The phone defaults are what an all-zero ceilings asks for.</summary>
    [Fact]
    public void The_phone_ceilings_are_all_zero()
    {
        Assert.Equal(default, Ceilings.Phone);
        Assert.NotEqual(default, Ceilings.Desktop);
    }
}
