using Boreas.Interop.Native;

namespace Boreas.Interop.Tunnel;

/// <summary>A Boreas call that did not succeed.</summary>
/// <remarks>
/// Carries the status rather than a message alone, so a caller can distinguish
/// the ones that mean something specific - an authority that did not restore, a
/// termination ceiling that is too low - from the ones that only mean "no".
/// </remarks>
public sealed class BoreasException(BoreasStatus status, string operation)
    : Exception($"{operation} failed: {status}.")
{
    public BoreasStatus Status { get; } = status;

    /// <summary>The call that failed, for a message a user can act on.</summary>
    public string Operation { get; } = operation;

    /// <summary>
    /// A defect in Boreas rather than a condition of the configuration or the
    /// network. The only supported next call on that handle is free.
    /// </summary>
    public bool IsDefect => Status is BoreasStatus.Panic;
}

/// <summary>
/// The header and the library came from different builds.
/// </summary>
/// <remarks>
/// Separate from <see cref="BoreasException"/> because it is not a failed call:
/// it is the check that runs before any call, and the only cheap moment to
/// notice. A stale library beside a newer header reads every field at the wrong
/// offset and behaves inexplicably.
/// </remarks>
public sealed class BoreasAbiMismatchException(uint compiled, uint loaded)
    : Exception(
        $"This build was compiled against Boreas ABI {compiled}, but the loaded boreas library "
        + $"implements ABI {loaded}. Install the boreas.dll that shipped with this build.")
{
    public uint Compiled { get; } = compiled;

    public uint Loaded { get; } = loaded;
}
