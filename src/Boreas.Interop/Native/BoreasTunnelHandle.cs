using System.Runtime.InteropServices;

namespace Boreas.Interop.Native;

/// <summary>
/// The opaque <c>BoreasTunnel *</c>, reference-counted across every call.
/// </summary>
/// <remarks>
/// <para>
/// A raw <c>nint</c> would work right up to the moment a finalizer ran during a
/// P/Invoke. Platform invoke increments a safe handle's reference count for the
/// duration of the call and decrements it on completion, which closes that race
/// and is the whole reason api/windows.md says wrapping is worth it.
/// </para>
/// <para>
/// <b>Disposing is not stopping.</b> <see cref="ReleaseHandle"/> is
/// <c>boreas_tunnel_free</c> alone. Teardown is three steps in order - shutdown,
/// join the reader, then free - because a thread blocked in
/// <c>next_event</c> holds a borrow of the handle and no amount of internal
/// locking fixes freeing it out from under a reader that is inside the call.
/// <c>NativeTunnel</c> owns that ordering; this type owns only the last step.
/// </para>
/// </remarks>
internal sealed class BoreasTunnelHandle : SafeHandle
{
    /// <summary>
    /// Required by the source-generated marshaller, which constructs the
    /// instance before the call and writes the native pointer into it
    /// afterwards.
    /// </summary>
    public BoreasTunnelHandle()
        : base(invalidHandleValue: nint.Zero, ownsHandle: true)
    {
    }

    /// <summary>
    /// A failed start leaves the out-parameter untouched, so the handle is
    /// still the zero this was constructed with, and nothing is freed.
    /// </summary>
    public override bool IsInvalid => handle == nint.Zero;

    /// <summary>
    /// The status is deliberately discarded, and this is the one place that is
    /// correct.
    /// </summary>
    /// <remarks>
    /// This runs on the finalizer thread as well as from <c>Dispose</c>. There
    /// is no caller to return a failure to and no recovery available: the
    /// handle is being reclaimed either way, and throwing from a finalizer
    /// takes the process down. <c>boreas_tunnel_free</c> also treats null as a
    /// no-op, so the only reachable failure is a defect that
    /// <see cref="BoreasStatus.Panic"/> already reported to whoever was still
    /// listening.
    /// </remarks>
    protected override bool ReleaseHandle()
    {
        _ = Native.Boreas.boreas_tunnel_free(handle);
        return true;
    }
}
