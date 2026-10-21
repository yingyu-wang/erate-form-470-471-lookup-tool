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
}
