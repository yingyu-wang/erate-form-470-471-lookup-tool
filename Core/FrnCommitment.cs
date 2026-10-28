namespace ERateLookup.Core;

/// <summary>
/// Columns from the public USAC Open Data FRN Status dataset (qdmp-ygft).
/// The tool does not sign in to EPC.
/// </summary>
public readonly record struct FrnCommitment(
    string FrnStatus,
    string CommittedAmount,
    string FcdlDate,
    string RevisedFcdlDate,
    string DisbursedAmount)
{
    public static readonly string[] Columns =
    [
        "FrnStatus",
        "CommittedAmount",
        "FcdlDate",
        "RevisedFcdlDate",
        "DisbursedAmount"
    ];

    public static FrnCommitment Blank { get; } = new("", "", "", "", "");

    /// <summary>
    /// Maps one published FRN Status row onto the export columns.
    /// </summary>
    /// <remarks>
    /// [Inference] CommittedAmount is <c>funding_commitment_request</c> when
    /// <c>form_471_frn_status_name</c> is Funded, and blank for every other
    /// status, including Pending, Denied, and Cancelled. Q8 is pending: Ying
    /// has not confirmed that this field equals the committed amount on an FCDL,
    /// including after <c>revised_fcdl_date</c>.
    /// FcdlDate, RevisedFcdlDate, and DisbursedAmount are the published fields
    /// <c>fcdl_letter_date</c>, <c>revised_fcdl_date</c>, and
    /// <c>total_authorized_disbursement</c>. A missing disbursement stays blank.
    /// It is not written as zero.
    /// </remarks>
    public static FrnCommitment FromPublished(
        string? frnStatus,
        string? fundingCommitmentRequest,
        string? fcdlLetterDate,
        string? revisedFcdlDate,
        string? totalAuthorizedDisbursement)
    {
        var status = frnStatus?.Trim() ?? "";
        var request = fundingCommitmentRequest?.Trim() ?? "";
        var committed = string.Equals(status, "Funded", StringComparison.OrdinalIgnoreCase) ? request : "";
        return new FrnCommitment(
            status,
            committed,
            UsacTimestamps.CalendarDate(fcdlLetterDate),
            UsacTimestamps.CalendarDate(revisedFcdlDate),
            totalAuthorizedDisbursement?.Trim() ?? "");
    }

    public string[] Cells() => [FrnStatus, CommittedAmount, FcdlDate, RevisedFcdlDate, DisbursedAmount];

    public const string StatusKind = "FRN status";

    /// <summary>
    /// Portfolio label for <see cref="CommittedAmount"/>. The export column name stays
    /// <c>CommittedAmount</c>. This label does not treat that value as a commitment decision.
    /// </summary>
    public const string ReportedCommittedAmountKind = "Reported committed amount";

    public const string FcdlDateKind = "FCDL date";

    public const string RevisedFcdlDateKind = "Revised FCDL date";

    public const string DisbursedAmountKind = "Disbursed amount";

    public const string ReportedAmountAssumption =
        "Reported committed amount is the published funding_commitment_request when the FRN status is Funded, and blank otherwise. It is not a funding commitment decision letter. Q8 is pending.";

    /// <summary>
    /// Field differences between two mapped FRN rows. Blank and whitespace match.
    /// A blank disbursement is not zero. <see cref="CommittedAmount"/> is reported
    /// under <see cref="ReportedCommittedAmountKind"/> and is not a commitment decision.
    /// </summary>
    public static IReadOnlyList<FrnFieldDiff> Compare(FrnCommitment before, FrnCommitment after)
    {
        var changes = new List<FrnFieldDiff>();
        Add(changes, StatusKind, before.FrnStatus, after.FrnStatus);
        Add(changes, ReportedCommittedAmountKind, before.CommittedAmount, after.CommittedAmount);
        Add(changes, FcdlDateKind, before.FcdlDate, after.FcdlDate);
        Add(changes, RevisedFcdlDateKind, before.RevisedFcdlDate, after.RevisedFcdlDate);
        Add(changes, DisbursedAmountKind, before.DisbursedAmount, after.DisbursedAmount);
        return changes;
    }

    public static string Normalize(string? value) => string.IsNullOrWhiteSpace(value) ? "" : value.Trim();

    static void Add(List<FrnFieldDiff> changes, string kind, string? before, string? after)
    {
        var left = Normalize(before);
        var right = Normalize(after);
        if (string.Equals(left, right, StringComparison.Ordinal))
            return;
        changes.Add(new FrnFieldDiff(kind, left, right));
    }
}

/// <summary>One reported FRN field difference. <see cref="FrnFieldDiff.Kind"/> is portfolio text.</summary>
public readonly record struct FrnFieldDiff(string Kind, string Before, string After);
