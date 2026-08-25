using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace Boreas.Interop.Wintun;

/// <summary>
/// Wintun 0.14.1, as <c>wintun.h</c> declares it.
/// </summary>
/// <remarks>
/// <para>
/// <b><c>SetLastError</c> is on for every declaration that can fail, and that
/// is not decoration.</b> Wintun reports through <c>GetLastError</c>, and the
/// source-generated P/Invoke does <b>not</b> capture the last error unless
/// asked: without this, <c>Marshal.GetLastWin32Error</c> returns whatever some
/// earlier unrelated call left behind, and the ring cannot tell "empty" from
/// "terminating" from "corrupt". The sample in api/windows.md calls
/// <c>GetLastWin32Error</c> after a <c>[LibraryImport]</c> that does not set it;
/// reported upstream.
/// </para>
/// <para>
/// No calling convention: <c>WINAPI</c> is <c>__stdcall</c>, and on x64 and
/// ARM64 there is only one convention, so naming it would be noise that could
/// later be wrong.
/// </para>
/// <para>
/// Strings are UTF-16 here, which is the opposite of every Boreas declaration.
/// Wintun takes <c>LPCWSTR</c>; Boreas takes UTF-8. Both are stated explicitly
/// so neither can be inferred from the other.
/// </para>
/// </remarks>
[SupportedOSPlatform("windows")]
internal static partial class Wintun
{
    private const string Library = "wintun";

    /// <summary>The ring is empty. Wait on the read-wait event and retry.</summary>
    public const int ErrorNoMoreItems = 259;

    /// <summary>The adapter is terminating. Not a transient condition.</summary>
    public const int ErrorHandleEof = 38;

    /// <summary>The send ring is full. The packet did not go.</summary>
    public const int ErrorBufferOverflow = 111;

    /// <summary>
    /// The largest packet Wintun will carry, from
    /// <c>WINTUN_MAX_IP_PACKET_SIZE</c>.
    /// </summary>
    public const int MaxIpPacketSize = 0xFFFF;

    [LibraryImport(Library, StringMarshalling = StringMarshalling.Utf16, SetLastError = true)]
    internal static partial nint WintunCreateAdapter(string name, string tunnelType, nint requestedGuid);

    [LibraryImport(Library, StringMarshalling = StringMarshalling.Utf16, SetLastError = true)]
    internal static partial nint WintunOpenAdapter(string name);

    [LibraryImport(Library)]
    internal static partial void WintunCloseAdapter(nint adapter);

    [LibraryImport(Library, SetLastError = true)]
    internal static partial nint WintunStartSession(WintunAdapterHandle adapter, uint capacity);

    [LibraryImport(Library)]
    internal static partial void WintunEndSession(nint session);

    /// <summary>
    /// The event signalled when the ring becomes readable.
    /// </summary>
    /// <remarks>
    /// <b>Never closed.</b> <c>wintun.h</c>: "Do not call CloseHandle on this
    /// event - it is managed by the session." The wrapper around it is
    /// therefore constructed as not owning its handle.
    /// </remarks>
    [LibraryImport(Library)]
    internal static partial nint WintunGetReadWaitEvent(WintunSessionHandle session);

    [LibraryImport(Library, SetLastError = true)]
    internal static partial nint WintunReceivePacket(WintunSessionHandle session, out uint packetSize);

    [LibraryImport(Library)]
    internal static partial void WintunReleaseReceivePacket(WintunSessionHandle session, nint packet);

    [LibraryImport(Library, SetLastError = true)]
    internal static partial nint WintunAllocateSendPacket(WintunSessionHandle session, uint packetSize);

    [LibraryImport(Library)]
    internal static partial void WintunSendPacket(WintunSessionHandle session, nint packet);

    [LibraryImport(Library, SetLastError = true)]
    internal static partial uint WintunGetRunningDriverVersion();
}

/// <summary>The adapter, closed after its session ends.</summary>
[SupportedOSPlatform("windows")]
internal sealed class WintunAdapterHandle : SafeHandle
{
    public WintunAdapterHandle()
        : base(invalidHandleValue: nint.Zero, ownsHandle: true)
    {
    }

    public WintunAdapterHandle(nint existing)
        : this() => SetHandle(existing);

    public override bool IsInvalid => handle == nint.Zero;

    protected override bool ReleaseHandle()
    {
        Wintun.WintunCloseAdapter(handle);
        return true;
    }
}

/// <summary>The session, ended before its adapter closes.</summary>
[SupportedOSPlatform("windows")]
internal sealed class WintunSessionHandle : SafeHandle
{
    public WintunSessionHandle()
        : base(invalidHandleValue: nint.Zero, ownsHandle: true)
    {
    }

    public WintunSessionHandle(nint existing)
        : this() => SetHandle(existing);

    public override bool IsInvalid => handle == nint.Zero;

    protected override bool ReleaseHandle()
    {
        Wintun.WintunEndSession(handle);
        return true;
    }
}
