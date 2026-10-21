using ERateLookup.Core;
using Xunit;

namespace ERateLookup.Tests;

public class FrnCommitmentTests
{
    [Fact]
    public void FundedCopiesTheCommitmentRequest()
    {
        var row = FrnCommitment.FromPublished("Funded", "14908.32", "2020-05-09T00:00:00.000", "", "14908.32");
        Assert.Equal("Funded", row.FrnStatus);
        Assert.Equal("14908.32", row.CommittedAmount);
        Assert.Equal("2020-05-09", row.FcdlDate);
        Assert.Equal("", row.RevisedFcdlDate);
        Assert.Equal("14908.32", row.DisbursedAmount);
    }

    [Fact]
    public void PendingKeepsTheRequestOutOfCommittedAmount()
    {
        var row = FrnCommitment.FromPublished("Pending", "14908.32", null, null, null);
        Assert.Equal("Pending", row.FrnStatus);
        Assert.Equal("", row.CommittedAmount);
        Assert.Equal("", row.FcdlDate);
        Assert.Equal("", row.RevisedFcdlDate);
        Assert.Equal("", row.DisbursedAmount);
    }

    [Fact]
    public void DeniedKeepsAZeroRequestOutOfCommittedAmount()
    {
        var row = FrnCommitment.FromPublished("Denied", "0", "2017-08-25T00:00:00.000", "", "");
        Assert.Equal("Denied", row.FrnStatus);
        Assert.Equal("", row.CommittedAmount);
        Assert.Equal("2017-08-25", row.FcdlDate);
        Assert.Equal("", row.DisbursedAmount);
    }

    [Fact]
    public void RevisedFcdlStillUsesTheFundedRequest()
    {
        // [Inference] A revised FCDL date does not change the mapping.
        // Q8 is pending: the request has not been confirmed against the letter.
        var row = FrnCommitment.FromPublished(
            "Funded",
            "14908.32",
            "2020-05-09T00:00:00.000",
            "2026-07-14T00:00:00.000",
            "14908.32");
        Assert.Equal(new[] { "Funded", "14908.32", "2020-05-09", "2026-07-14", "14908.32" }, row.Cells());
    }

    [Fact]
    public void CancelledIsNotACommittedAmount()
    {
        var row = FrnCommitment.FromPublished("Cancelled", "0", "2023-09-01T00:00:00.000", "2024-07-12T00:00:00.000", "");
        Assert.Equal("", row.CommittedAmount);
        Assert.Equal("2023-09-01", row.FcdlDate);
        Assert.Equal("2024-07-12", row.RevisedFcdlDate);
    }

    [Fact]
    public void BlankDisbursementStaysBlank()
    {
        var row = FrnCommitment.FromPublished("funded", "10", null, null, "  ");
        Assert.Equal("10", row.CommittedAmount);
        Assert.Equal("", row.DisbursedAmount);
    }
}
