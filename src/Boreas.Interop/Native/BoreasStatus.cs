namespace Boreas.Interop.Native;

/// <summary>
/// Every way a Boreas call can end, as <c>boreas.h</c> numbers them.
/// </summary>
/// <remarks>
/// Zero is success, so <c>if (call(...)) { failed }</c> reads correctly in C.
/// Nothing signals failure any other way and nothing sets <c>errno</c>, which
/// is why no declaration in <see cref="Boreas"/> sets <c>SetLastError</c>.
/// </remarks>
public enum BoreasStatus
{
    Ok = 0,

    /// <summary>A required pointer was null. Always a bug in the caller.</summary>
    NullArgument = 1,

    /// <summary>A string argument was not valid UTF-8.</summary>
    NotUtf8 = 2,

    /// <summary>The configuration describes a tunnel that cannot exist.</summary>
    Config = 3,

    /// <summary>
    /// Stored authority material was lost, corrupted, or is not two halves of
    /// one authority.
    /// </summary>
    Authority = 4,

    /// <summary>An egress could not be built from its configuration.</summary>
    Egress = 5,

    /// <summary>
    /// The connection ceiling cannot hold a listening backlog for every
    /// inspected port.
    /// </summary>
    Termination = 6,

    /// <summary>The datapath refused the combination it was handed.</summary>
    Datapath = 7,

    /// <summary>A socket the tunnel needs could not be opened through the bypass.</summary>
    Io = 8,

    /// <summary>The tunnel has stopped. The handle is still valid to free.</summary>
    Stopped = 9,

    /// <summary>An output buffer was too small; the length out-parameter says how small.</summary>
    BufferTooSmall = 10,

    /// <summary>
    /// A panic was caught at the boundary. Always a defect in Boreas: free the
    /// handle and report it, and do not retry on it.
    /// </summary>
    Panic = 11,

    /// <summary>A failure this build predates.</summary>
    Unrecognised = 12,
}

/// <summary>
/// The fold that closes <see cref="BoreasStatus"/> over an untrusted integer.
/// </summary>
/// <remarks>
/// A C# enum is an open type: a value native code invents is stored in one
/// without complaint, and every <c>switch</c> over it then falls through its
/// default arm carrying a number nothing can name. api/stability.md reserves
/// the right to add a constant at the next unused value and tells hosts to
/// "handle a value you do not recognise rather than asserting exhaustiveness",
/// so this is the one place a status from native code becomes a value the rest
/// of the assembly may treat as closed.
/// </remarks>
public static class BoreasStatusValues
{
    /// <summary>
    /// The declared statuses, in ABI order. Also the evidence for the range
    /// check below: <c>StatusLaws</c> asserts this array is exactly the
    /// declared set and that its values run 0..12 without a gap.
    /// </summary>
    public static readonly BoreasStatus[] All =
    [
        BoreasStatus.Ok,
        BoreasStatus.NullArgument,
        BoreasStatus.NotUtf8,
        BoreasStatus.Config,
        BoreasStatus.Authority,
        BoreasStatus.Egress,
        BoreasStatus.Termination,
        BoreasStatus.Datapath,
        BoreasStatus.Io,
        BoreasStatus.Stopped,
        BoreasStatus.BufferTooSmall,
        BoreasStatus.Panic,
        BoreasStatus.Unrecognised,
    ];

    extension(BoreasStatus status)
    {
        /// <summary>
        /// The same status, or <see cref="BoreasStatus.Unrecognised"/> when it
        /// is not one this build declares.
        /// </summary>
        /// <remarks>
        /// A contiguous range check rather than <c>Enum.IsDefined</c>: the
        /// values are dense from zero, so the comparison is two branches and no
        /// metadata lookup. Density is not assumed, it is asserted by the law
        /// over <see cref="All"/>.
        /// </remarks>
        public BoreasStatus Recognised =>
            status is >= BoreasStatus.Ok and <= BoreasStatus.Unrecognised
                ? status
                : BoreasStatus.Unrecognised;

        /// <summary>True only for <see cref="BoreasStatus.Ok"/>.</summary>
        public bool IsOk => status is BoreasStatus.Ok;
    }
}
