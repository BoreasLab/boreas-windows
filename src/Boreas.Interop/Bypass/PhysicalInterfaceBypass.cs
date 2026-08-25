using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using Boreas.Interop.Native;

namespace Boreas.Interop.Bypass;

/// <summary>
/// Excludes one socket from the tunnel by naming the physical interface it must
/// leave by.
/// </summary>
/// <remarks>
/// <para>
/// There is no <c>VpnService.protect</c> on Windows. The equivalent is
/// <c>IP_UNICAST_IF</c> / <c>IPV6_UNICAST_IF</c>, which
/// <see cref="UnicastInterface"/> owns.
/// </para>
/// <para>
/// <b>The index is mutable and read on every call.</b> A laptop moving from
/// Wi-Fi to Ethernet changes it, and a bypass holding the index it was
/// constructed with would keep binding sockets to an interface that no longer
/// carries traffic. <c>protect</c> may also be called concurrently with itself
/// and with either device callback, so the read is volatile rather than merely
/// a field access.
/// </para>
/// </remarks>
public sealed class PhysicalInterfaceBypass
{
    /// <summary>
    /// How a socket is bound to an interface. Injected so the vtable's
    /// plumbing can be driven without Winsock; the default is the real thing.
    /// </summary>
    /// <returns>Zero on success, negative on refusal.</returns>
    public delegate int Binder(long socket, uint interfaceIndex);

    private readonly Binder _bind;
    private uint _index;

    [SupportedOSPlatform("windows")]
    public PhysicalInterfaceBypass(uint interfaceIndex)
        : this(interfaceIndex, UnicastInterface.Bind)
    {
    }

    public PhysicalInterfaceBypass(uint interfaceIndex, Binder bind)
    {
        _index = interfaceIndex;
        _bind = bind;
    }

    /// <summary>
    /// The interface every protected socket is bound to, as of now.
    /// </summary>
    /// <remarks>
    /// Written when Windows reports the adapter set changed. Sockets already
    /// connected keep the interface they were bound to; the core opens a fresh
    /// socket per dial, so the new value reaches everything that matters
    /// without anything being torn down here.
    /// </remarks>
    public uint InterfaceIndex
    {
        get => Volatile.Read(ref _index);
        set => Volatile.Write(ref _index, value);
    }

    /// <summary>
    /// Excludes one socket, or refuses.
    /// </summary>
    /// <remarks>
    /// A <c>protect</c> that returns zero without doing anything is the bug
    /// obligations.md names, written out. Index zero is not an interface, so it
    /// is refused rather than passed to Winsock, which would accept it as
    /// "unspecified" and leave the socket on the default route - the tunnel.
    /// </remarks>
    public int Protect(long socket)
    {
        var index = InterfaceIndex;

        return index == 0 ? -1 : _bind(socket, index);
    }
}

/// <summary>
/// The <see cref="BoreasBypass"/> vtable over a
/// <see cref="PhysicalInterfaceBypass"/>.
/// </summary>
/// <remarks>
/// Same ownership as <see cref="Device.TunDevice"/>: the object travels as the
/// context and release is its only destructor, called exactly once whatever
/// <c>boreas_tunnel_start</c> returns.
/// </remarks>
internal static unsafe class BypassVtable
{
    public static BoreasBypass For(PhysicalInterfaceBypass bypass) => new()
    {
        Context = GCHandle.ToIntPtr(GCHandle.Alloc(bypass)),
        Protect = &Protect,
        Release = &Release,
    };

    /// <summary>
    /// Runs the release a failed handover would otherwise have skipped. Only
    /// for a vtable that was never passed to <c>boreas_tunnel_start</c>.
    /// </summary>
    public static void Abandon(BoreasBypass bypass) => ReleaseContext(bypass.Context);

    [UnmanagedCallersOnly]
    private static int Protect(nint context, long socket)
    {
        try
        {
            return ((PhysicalInterfaceBypass)GCHandle.FromIntPtr(context).Target!).Protect(socket);
        }
        catch
        {
            // Refusing is the safe answer. A socket reported as protected when
            // it is not re-enters the tunnel silently, and nothing downstream
            // can detect that.
            return -1;
        }
    }

    [UnmanagedCallersOnly]
    private static void Release(nint context) => ReleaseContext(context);

    /// <summary>
    /// The body, reachable from managed code. See the note on
    /// <c>TunDevice.ReleaseContext</c> for why it is split out.
    /// </summary>
    private static void ReleaseContext(nint context)
    {
        if (context == nint.Zero)
        {
            return;
        }

        GCHandle.FromIntPtr(context).Free();
    }
}
