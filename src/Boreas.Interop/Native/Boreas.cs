using System.Runtime.InteropServices;

namespace Boreas.Interop.Native;

/// <summary>
/// The six tunnel functions and the version check, exactly as
/// <c>ffi/include/boreas.h</c> declares them.
/// </summary>
/// <remarks>
/// <para>
/// <c>[LibraryImport]</c> rather than <c>[DllImport]</c>: it generates the
/// marshalling at compile time, which is what makes these work under trimming
/// and AOT, and it refuses a declaration that is not blittable, which is the
/// diagnostic that catches the type traps below before a byte moves.
/// </para>
/// <para>
/// <b>No calling convention.</b> On x64 and ARM64 there is only one, so naming
/// it would be noise that could later be wrong.
/// </para>
/// <para>
/// <b>No <c>SetLastError</c>.</b> Every function returns a
/// <see cref="BoreasStatus"/>; nothing signals failure any other way and
/// nothing sets <c>errno</c>. Asking the runtime to capture a last-error value
/// after every call would cost a transition to buy a number that is never
/// meaningful. Wintun is the opposite case and its declarations say so.
/// </para>
/// <para>
/// <b>Every string is UTF-8.</b> These declarations take <c>byte*</c> rather
/// than <c>string</c> so no marshaller can choose an encoding: a UTF-16 string
/// reaches Boreas as mojibake or <see cref="BoreasStatus.NotUtf8"/>. Encoding
/// happens once, in <see cref="Utf8Block"/>.
/// </para>
/// <para>
/// <b>Internal on purpose.</b> C declares every one of these
/// <c>nodiscard</c> and C# has no equivalent, so the enforcement here is
/// visibility: nothing outside this assembly can call one, and every wrapper
/// that does consumes the status it returns.
/// </para>
/// </remarks>
internal static unsafe partial class Boreas
{
    /// <summary>The name .NET resolves from <c>runtimes/&lt;rid&gt;/native/</c>.</summary>
    private const string Library = "boreas";

    /// <summary>
    /// The ABI version this build was compiled against, from
    /// <c>BOREAS_ABI_VERSION</c>.
    /// </summary>
    /// <remarks>
    /// Compared against <see cref="boreas_abi_version"/> at startup, before
    /// anything else. A stale library beside a newer header reads every field
    /// at the wrong offset and behaves inexplicably; this is the only cheap
    /// moment to notice.
    /// </remarks>
    public const uint CompiledAbiVersion = 1;

    /// <summary>
    /// The ABI version the loaded library actually implements.
    /// </summary>
    /// <remarks>
    /// This is the one function api/windows.md#the-declarations never declares,
    /// although api/artifacts.md and api/abi.md both require calling it. It is
    /// declared here from <c>boreas.h</c>, which is the source of truth and
    /// ships in the archive for exactly that reason.
    /// </remarks>
    [LibraryImport(Library)]
    internal static partial uint boreas_abi_version();

    /// <summary>
    /// Builds and starts everything, writing the handle through
    /// <paramref name="outHandle"/>.
    /// </summary>
    /// <remarks>
    /// On any failure nothing is allocated and the out-parameter is untouched,
    /// but <b>both release callbacks still run</b>, so a context handed over is
    /// always accounted for and the caller may retry with a fresh one. Blocks
    /// for as long as the first connection takes.
    /// </remarks>
    [LibraryImport(Library)]
    internal static partial BoreasStatus boreas_tunnel_start(
        BoreasConfig* config,
        BoreasDevice* device,
        BoreasBypass* bypass,
        out BoreasTunnelHandle outHandle);

    /// <summary>
    /// Blocks until the next event, or <see cref="BoreasStatus.Stopped"/> once
    /// none can arrive.
    /// </summary>
    /// <remarks>
    /// <b>This can block for hours.</b> A healthy idle tunnel emits nothing:
    /// counters are reported only when non-zero, so "nothing went wrong" is
    /// silence. It needs a dedicated thread, never one from the pool. Every
    /// other entry point may be called while it is blocked.
    /// </remarks>
    [LibraryImport(Library)]
    internal static partial BoreasStatus boreas_tunnel_next_event(
        BoreasTunnelHandle handle,
        BoreasEvent* @event,
        byte* name,
        nuint nameCap,
        byte* rule,
        nuint ruleCap);

    /// <summary>
    /// Replaces the rules in force without restarting or dropping a connection.
    /// Takes a whole list set, never a delta.
    /// </summary>
    [LibraryImport(Library)]
    internal static partial BoreasStatus boreas_tunnel_reload(
        BoreasTunnelHandle handle,
        byte** lists,
        nuint count,
        BoreasEvent* result);

    /// <summary>
    /// Copies out the certificate authority's material. Call twice: once with
    /// both capacities zero to learn the lengths, then again to fill.
    /// </summary>
    /// <remarks>
    /// Both lengths zero means this tunnel does not intercept, which is an
    /// answer rather than a failure.
    /// </remarks>
    [LibraryImport(Library)]
    internal static partial BoreasStatus boreas_tunnel_authority(
        BoreasTunnelHandle handle,
        byte* certificate,
        nuint certificateCap,
        nuint* certificateLen,
        byte* keys,
        nuint keysCap,
        nuint* keysLen);

    /// <summary>
    /// Stops carrying traffic and releases any thread blocked in
    /// <see cref="boreas_tunnel_next_event"/>.
    /// </summary>
    /// <remarks>
    /// Idempotent and safe from any thread, so a teardown path never has to
    /// remember whether it already ran.
    /// </remarks>
    [LibraryImport(Library)]
    internal static partial BoreasStatus boreas_tunnel_shutdown(BoreasTunnelHandle handle);

    /// <summary>
    /// Frees the handle. Passing null is a no-op.
    /// </summary>
    /// <remarks>
    /// Takes a raw pointer rather than the safe handle because its one caller
    /// is <see cref="BoreasTunnelHandle.ReleaseHandle"/>, which runs when the
    /// safe handle is already being torn down and cannot marshal itself.
    /// </remarks>
    [LibraryImport(Library)]
    internal static partial BoreasStatus boreas_tunnel_free(nint handle);
}
