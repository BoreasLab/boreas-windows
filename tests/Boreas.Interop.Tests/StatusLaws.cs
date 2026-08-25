using Boreas.Interop.Native;

namespace Boreas.Interop.Tests;

/// <summary>
/// Laws for the fold that closes an open enum over an untrusted integer.
/// </summary>
public sealed class StatusLaws
{
    /// <summary>
    /// <c>Recognised</c> is a range check, and a range check is only sound over
    /// a dense enum. This is the evidence for that soundness, so a constant
    /// added out of order fails here rather than being silently accepted as a
    /// status this build has never heard of.
    /// </summary>
    [Fact]
    public void The_declared_statuses_run_from_zero_without_a_gap()
    {
        var declared = Enum.GetValues<BoreasStatus>();

        Assert.Equal(declared.Length, BoreasStatusValues.All.Length);
        Assert.Equal(declared.Order().ToArray(), BoreasStatusValues.All.Order().ToArray());

        for (var index = 0; index < BoreasStatusValues.All.Length; index++)
        {
            Assert.Equal(index, (int)BoreasStatusValues.All[index]);
        }
    }

    /// <summary>
    /// api/stability.md reserves adding a constant at the next unused value and
    /// tells hosts to handle one they do not recognise. Every such value has to
    /// arrive as <see cref="BoreasStatus.Unrecognised"/>, including a negative
    /// one, which no version of the ABI will ever define.
    /// </summary>
    [Theory]
    [InlineData(13)]
    [InlineData(14)]
    [InlineData(int.MaxValue)]
    [InlineData(-1)]
    [InlineData(int.MinValue)]
    public void A_status_this_build_predates_folds_to_unrecognised(int raw) =>
        Assert.Equal(BoreasStatus.Unrecognised, ((BoreasStatus)raw).Recognised);

    /// <summary>Every declared status survives the fold unchanged.</summary>
    [Fact]
    public void A_declared_status_passes_through_the_fold()
    {
        foreach (var status in BoreasStatusValues.All)
        {
            Assert.Equal(status, status.Recognised);
        }
    }

    /// <summary>
    /// Zero is success, so the C idiom reads correctly and nothing else does.
    /// </summary>
    [Fact]
    public void Only_zero_is_success()
    {
        Assert.Equal(0, (int)BoreasStatus.Ok);

        foreach (var status in BoreasStatusValues.All)
        {
            Assert.Equal(status is BoreasStatus.Ok, status.IsOk);
        }
    }
}
