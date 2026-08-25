using System.Reflection;
using Boreas.Interop.Native;

namespace Boreas.Interop.Tests;

/// <summary>
/// Every field and every declaration, diffed against <c>boreas.h</c> by name,
/// order, and width.
/// </summary>
/// <remarks>
/// <para>
/// <see cref="AbiLayout"/> pins where each field sits, and that is not enough.
/// The second type trap - <c>size_t</c> declared as <c>uint</c> instead of
/// <c>nuint</c> - moves <b>nothing</b>: in every struct here the four bytes it
/// loses fall into padding the following field needed anyway, so every offset
/// and every size comes out identical while native writes eight bytes and C#
/// reads four. Only a statement about widths catches it, which is what this
/// class is.
/// </para>
/// <para>
/// Written as whole-sequence comparisons rather than one assertion per field so
/// that a field added, removed, or reordered fails as loudly as one retyped.
/// </para>
/// </remarks>
public sealed class AbiSignatures
{
    /// <summary>
    /// A type as the header spells it, so a failure message reads like the
    /// header rather than like reflection.
    /// </summary>
    private static string Describe(Type type)
    {
        if (type.IsFunctionPointer)
        {
            var parameters = type.GetFunctionPointerParameterTypes().Select(Describe);
            return $"delegate* unmanaged<{string.Join(", ", parameters.Append(Describe(type.GetFunctionPointerReturnType())))}>";
        }

        return type.IsPointer ? Describe(type.GetElementType()!) + "*"
            : type == typeof(nint) ? "nint"
            : type == typeof(nuint) ? "nuint"
            : type.Name;
    }

    /// <summary>Declared instance fields, in metadata order, as "name: type".</summary>
    private static string[] FieldsOf<T>() =>
        [.. typeof(T)
            .GetFields(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)
            .Select(field => $"{field.Name}: {Describe(field.FieldType)}")];

    /// <summary>
    /// The TUN vtable. Both callback signatures take <c>size_t</c> for the
    /// buffer capacity and return <c>intptr_t</c>, and both are hand-filled, so
    /// a wrong one here is a call through the wrong function pointer rather
    /// than a compile error.
    /// </summary>
    [Fact]
    public void The_device_vtable_is_declared_as_the_header_declares_it() =>
        Assert.Equal(
            [
                "Context: nint",
                "Recv: delegate* unmanaged<nint, Byte*, nuint, nint>",
                "Send: delegate* unmanaged<nint, Byte*, nuint, nint>",
                "Close: delegate* unmanaged<nint, Void>",
                "Release: delegate* unmanaged<nint, Void>",
                "Mtu: UInt16",
            ],
            FieldsOf<BoreasDevice>());

    /// <summary>
    /// The bypass vtable. <c>BoreasSocket</c> is <c>int64_t</c> because a file
    /// descriptor is an <c>int</c> and a Windows <c>SOCKET</c> is an unsigned
    /// pointer-width handle, and one type has to hold both.
    /// </summary>
    [Fact]
    public void The_bypass_vtable_is_declared_as_the_header_declares_it() =>
        Assert.Equal(
            [
                "Context: nint",
                "Protect: delegate* unmanaged<nint, Int64, Int32>",
                "Release: delegate* unmanaged<nint, Void>",
            ],
            FieldsOf<BoreasBypass>());

    /// <summary>Six <c>size_t</c>, which is six <c>nuint</c> and never six <c>uint</c>.</summary>
    [Fact]
    public void The_ceilings_are_six_pointer_width_values() =>
        Assert.Equal(
            [
                "BufferSlices: nuint",
                "DatagramsPerFlow: nuint",
                "TerminatedConnections: nuint",
                "Associations: nuint",
                "InspectedAddresses: nuint",
                "PendingReassemblies: nuint",
            ],
            FieldsOf<BoreasCeilings>());

    /// <summary>Six <c>uint64_t</c>, which stay 64 bits on every architecture.</summary>
    [Fact]
    public void The_counters_are_six_fixed_width_values() =>
        Assert.Equal(
            [
                "DatagramsDropped: UInt64",
                "PacketsRejected: UInt64",
                "QuicSteered: UInt64",
                "PathsReported: UInt64",
                "EventsLost: UInt64",
                "TasksPanicked: UInt64",
            ],
            FieldsOf<BoreasCounters>());

    [Fact]
    public void The_wireguard_peer_is_declared_as_the_header_declares_it() =>
        Assert.Equal(
            [
                "Endpoint: nint",
                "PrivateKey: BoreasKey",
                "PeerPublicKey: BoreasKey",
                "PresharedKey: BoreasKey",
                "HasPresharedKey: Boolean",
            ],
            FieldsOf<BoreasWireGuard>());

    /// <summary>
    /// Five of these are <c>size_t</c> and five are pointers, alternating. The
    /// alternation is why a single retyped one is invisible to every offset
    /// law: it lands in the padding the pointer beside it already required.
    /// </summary>
    [Fact]
    public void The_config_is_declared_as_the_header_declares_it() =>
        Assert.Equal(
            [
                "Egress: BoreasEgress",
                "WireGuard: BoreasWireGuard",
                "NatBehavior: BoreasNat",
                "Resolver: nint",
                "Lists: nint",
                "ListCount: nuint",
                "InterceptHosts: nint",
                "InterceptHostCount: nuint",
                "RootCertificate: nint",
                "RootCertificateLen: nuint",
                "AuthorityKeys: nint",
                "AuthorityKeysLen: nuint",
                "RewriteDocuments: Boolean",
                "Mtu: UInt16",
                "Ceilings: BoreasCeilings",
            ],
            FieldsOf<BoreasConfig>());

    /// <summary>
    /// Five <c>size_t</c> in a row after the tag. This is the struct the ABI
    /// fills in, so a narrow read here is a truncated length that reports a
    /// name as having fit when it did not.
    /// </summary>
    [Fact]
    public void The_event_is_declared_as_the_header_declares_it() =>
        Assert.Equal(
            [
                "Kind: BoreasEventKind",
                "Blocked: Boolean",
                "NameLen: nuint",
                "RuleLen: nuint",
                "Allowed: nuint",
                "BlockedRules: nuint",
                "Inspected: nuint",
                "Counters: BoreasCounters",
            ],
            FieldsOf<BoreasEvent>());

    /// <summary>
    /// The seven exported functions, by name and full signature.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The capacities in <c>next_event</c> and <c>authority</c> are
    /// <c>size_t</c> and the lengths come back through <c>size_t *</c>. Declared
    /// as <c>uint</c> they compile, run, and hand Boreas half a capacity on a
    /// 64-bit host.
    /// </para>
    /// <para>
    /// The count is asserted too. <c>boreas_abi_version</c> is the seventh, and
    /// api/windows.md#the-declarations omits it although api/artifacts.md
    /// requires calling it before anything else; this is the assertion that
    /// keeps it from being dropped again.
    /// </para>
    /// </remarks>
    [Fact]
    public void The_declarations_match_the_header()
    {
        var declared = typeof(Boreas.Interop.Native.Boreas)
            .GetMethods(BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.Static | BindingFlags.DeclaredOnly)
            .Where(method => method.GetCustomAttribute<System.Runtime.InteropServices.LibraryImportAttribute>() is not null)
            .Select(method =>
                $"{Describe(method.ReturnType)} {method.Name}("
                + string.Join(", ", method.GetParameters().Select(p => Describe(p.ParameterType)))
                + ")")
            .Order()
            .ToArray();

        Assert.Equal(
            [
                "BoreasStatus boreas_tunnel_authority(BoreasTunnelHandle, Byte*, nuint, nuint*, Byte*, nuint, nuint*)",
                "BoreasStatus boreas_tunnel_free(nint)",
                "BoreasStatus boreas_tunnel_next_event(BoreasTunnelHandle, BoreasEvent*, Byte*, nuint, Byte*, nuint)",
                "BoreasStatus boreas_tunnel_reload(BoreasTunnelHandle, Byte**, nuint, BoreasEvent*)",
                "BoreasStatus boreas_tunnel_shutdown(BoreasTunnelHandle)",
                "BoreasStatus boreas_tunnel_start(BoreasConfig*, BoreasDevice*, BoreasBypass*, BoreasTunnelHandle&)",
                "UInt32 boreas_abi_version()",
            ],
            declared);
    }

    /// <summary>
    /// The version this build compiled against is the header's
    /// <c>BOREAS_ABI_VERSION</c>. Startup compares it against what the loaded
    /// library reports and refuses on a mismatch; if this constant drifts from
    /// the header, that check compares the wrong number and passes.
    /// </summary>
    [Fact]
    public void The_compiled_abi_version_is_the_headers() =>
        Assert.Equal(1u, Boreas.Interop.Native.Boreas.CompiledAbiVersion);
}
