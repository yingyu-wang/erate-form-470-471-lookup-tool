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

    [Fact]
    public void CompareLabelsCommittedAmountAsReportedAndIgnoresUnchangedNulls()
    {
        var pending = FrnCommitment.FromPublished("Pending", "14908.32", null, null, null);
        var same = FrnCommitment.FromPublished("Pending", "999.00", "  ", null, "");
        Assert.Empty(FrnCommitment.Compare(pending, same));
        Assert.Equal("", pending.CommittedAmount);

        var funded = FrnCommitment.FromPublished("Funded", "14908.32", "2020-05-09T00:00:00.000", null, null);
        var revised = FrnCommitment.FromPublished("Funded", "14000.00", "2020-05-10T00:00:00.000", "2026-07-14T00:00:00.000", "10");
        var changes = FrnCommitment.Compare(funded, revised);
        Assert.Equal(
            new[] { "Reported committed amount", "FCDL date", "Revised FCDL date", "Disbursed amount" },
            changes.Select(change => change.Kind).ToArray());
        Assert.Equal(("14908.32", "14000.00"), (changes[0].Before, changes[0].After));
        Assert.Equal(("2020-05-09", "2020-05-10"), (changes[1].Before, changes[1].After));
        Assert.Equal(("", "2026-07-14"), (changes[2].Before, changes[2].After));
        Assert.Equal(("", "10"), (changes[3].Before, changes[3].After));
        Assert.All(changes, change => Assert.DoesNotContain("decision", change.Kind, StringComparison.OrdinalIgnoreCase));
        Assert.Contains("not a funding commitment decision letter", FrnCommitment.ReportedAmountAssumption, StringComparison.Ordinal);
        Assert.Equal("CommittedAmount", FrnCommitment.Columns[1]);
    }

    [Fact]
    public void StatusTransitionDoesNotInventACommitmentDecision()
    {
        var denied = FrnCommitment.FromPublished("Denied", "0", "2017-08-25T00:00:00.000", "", "");
        var cancelled = FrnCommitment.FromPublished("Cancelled", "0", "2017-08-25T00:00:00.000", "", null);
        var only = Assert.Single(FrnCommitment.Compare(denied, cancelled));
        Assert.Equal("FRN status", only.Kind);
        Assert.Equal("Denied", only.Before);
        Assert.Equal("Cancelled", only.After);

        var pending = FrnCommitment.FromPublished("Pending", "10", null, null, null);
        var funded = FrnCommitment.FromPublished("Funded", "10", null, null, "0");
        var both = FrnCommitment.Compare(pending, funded);
        Assert.Contains(both, change => change.Kind == "FRN status" && change.Before == "Pending" && change.After == "Funded");
        Assert.Contains(both, change => change.Kind == "Reported committed amount" && change.Before == "" && change.After == "10");
        Assert.Contains(both, change => change.Kind == "Disbursed amount" && change.Before == "" && change.After == "0");
        Assert.DoesNotContain(both, change => change.Kind.Contains("commitment decision", StringComparison.OrdinalIgnoreCase));
    }
}
