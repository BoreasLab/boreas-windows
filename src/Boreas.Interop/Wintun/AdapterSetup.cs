using System.Diagnostics;
using System.Globalization;
using System.Net;
using System.Runtime.Versioning;
using Boreas.Interop.Tunnel;

namespace Boreas.Interop.Wintun;

/// <summary>
/// Configures the Windows interface a Wintun adapter presents.
/// </summary>
/// <remarks>
/// <para>
/// <b>Verified: <c>wintun.h</c> does not mention MTU at all.</b> A
/// case-insensitive search of the current header returns nothing, and neither
/// does wintun.net. api/windows.md recorded that as unconfirmed and sourced
/// from a mailing list; it is now confirmed from the primary source. Wintun
/// creates an adapter and nothing else - no address, no MTU, no DNS - so all of
/// that is the host's, and it is here.
/// </para>
/// <para>
/// <b>Why netsh rather than the IP Helper API.</b> api/windows.md offers both.
/// <c>SetIpInterfaceEntry</c> is the better call and it takes a
/// <c>MIB_IPINTERFACE_ROW</c>: about thirty fields including two enums whose
/// width is implementation-defined and a sixteen-element array, all of which a
/// C# host transcribes by hand. There is no Windows machine in this
/// repository's build or test path to check that transcription against, and
/// getting it wrong is not a refused call - it is <c>GetIpInterfaceEntry</c>
/// filling a buffer, this code writing at the wrong offset, and
/// <c>SetIpInterfaceEntry</c> applying a corrupted row to a live interface.
/// netsh cannot do that. It is slower and it is a process, and both are cheap
/// beside a struct nobody here can check.
/// </para>
/// <para>
/// <b>This is the one place in this assembly where the better engineering was
/// refused for want of a machine to check it on.</b> It is contained in one
/// type so replacing it is a contained change for whoever has one.
/// </para>
/// </remarks>
[SupportedOSPlatform("windows")]
public static class AdapterSetup
{
    /// <summary>
    /// Gives the interface its address, its MTU, and its DNS servers.
    /// </summary>
    /// <param name="dns">
    /// Empty keeps whatever Windows already uses, which is a real answer rather
    /// than an omission.
    /// </param>
    /// <exception cref="InvalidOperationException">
    /// The interface was not configured. Thrown rather than returned because
    /// there is nothing to continue to: an adapter without its address carries
    /// nothing, and one whose MTU does not match the tunnel's is the
    /// misconfiguration <c>paths_reported</c> reports forever.
    /// </exception>
    public static void Apply(string interfaceName, IPAddress address, int prefixLength, Mtu mtu, IReadOnlyList<IPAddress> dns)
    {
        var family = AdapterAddress.IsIPv6(address) ? "ipv6" : "ipv4";

        // The two families do not share a spelling: the IPv4 form names the
        // interface with name= and takes a dotted mask, the IPv6 form names it
        // with interface= and takes the prefix length as written.
        Netsh(AdapterAddress.IsIPv6(address)
            ? ["interface", "ipv6", "set", "address",
               $"interface={interfaceName}", $"address={address}/{prefixLength}"]
            : ["interface", "ipv4", "set", "address",
               $"name={interfaceName}", "source=static",
               $"address={address}", $"mask={AdapterAddress.Mask(prefixLength)}"]);

        // Both families, because the tunnel carries both: an IPv6 interface
        // left at the default would answer Packet Too Big for traffic the IPv4
        // side was configured to carry, which is the same symptom as telling
        // the two sides different numbers, arriving by another route.
        foreach (var each in (ReadOnlySpan<string>)["ipv4", "ipv6"])
        {
            Netsh([
                "interface", each, "set", "subinterface", interfaceName,
                $"mtu={mtu.Value.ToString(CultureInfo.InvariantCulture)}", "store=persistent",
            ]);
        }

        ApplyDns(interfaceName, family, dns);
    }

    private static void ApplyDns(string interfaceName, string family, IReadOnlyList<IPAddress> dns)
    {
        if (dns.Count == 0)
        {
            return;
        }

        // register=none: this is a tunnel interface, and registering its
        // address in DNS would publish where the device is to whatever the
        // tunnel was meant to keep it from.
        Netsh([
            "interface", family, "set", "dnsservers", $"name={interfaceName}",
            "source=static", $"address={dns[0]}", "register=none", "validate=no",
        ]);

        // Index is one-based and the first is already set, so the rest start at
        // two and keep the order the user wrote them in - which is the order
        // the resolver tries them in.
        for (var position = 1; position < dns.Count; position++)
        {
            Netsh([
                "interface", family, "add", "dnsservers", $"name={interfaceName}",
                $"address={dns[position]}",
                $"index={(position + 1).ToString(CultureInfo.InvariantCulture)}", "validate=no",
            ]);
        }
    }

    private static void Netsh(params string[] arguments)
    {
        // ArgumentList quotes each argument for the platform, so an adapter
        // name with a space in it is one argument rather than a quoting bug
        // waiting for the first user who renames their adapter.
        var start = new ProcessStartInfo("netsh")
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };

        foreach (var argument in arguments)
        {
            start.ArgumentList.Add(argument);
        }

        using var process = Process.Start(start)
            ?? throw new InvalidOperationException("Could not run netsh.");

        var output = process.StandardOutput.ReadToEnd();
        var error = process.StandardError.ReadToEnd();

        process.WaitForExit();

        if (process.ExitCode != 0)
        {
            throw new InvalidOperationException(
                $"netsh {string.Join(' ', arguments)} exited {process.ExitCode}. "
                + $"{output.Trim()} {error.Trim()}".Trim());
        }
    }
}
