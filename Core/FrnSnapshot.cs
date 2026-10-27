namespace ERateLookup.Core;

/// <summary>
/// One published FRN Status row stored for a tracked BEN and funding year.
/// </summary>
/// <remarks>
/// <para>
/// <see cref="ReportedCommittedAmount"/> is the export column <c>CommittedAmount</c>
/// produced by <see cref="FrnCommitment.FromPublished"/>. It is
/// <c>funding_commitment_request</c> when the published status is Funded, and
/// blank otherwise. It is not a funding commitment decision letter.
/// Q8 is pending: Ying has not confirmed that value against an FCDL, including
/// when a revised FCDL date is present. Portfolio text calls it a reported
/// committed amount and does not derive a commitment decision from it.
/// </para>
/// <para>
/// <see cref="FrnStatus"/>, <see cref="FcdlDate"/>, <see cref="RevisedFcdlDate"/>,
/// and <see cref="DisbursedAmount"/> are the published fields. A missing
/// disbursement or date stays blank. It is not stored as zero.
/// </para>
/// </remarks>
public sealed record ReportedFrn(
    string FundingRequestNumber,
    string ApplicationNumber,
    string Name,
    string FrnStatus,
    string ReportedCommittedAmount,
    string FcdlDate,
    string RevisedFcdlDate,
    string DisbursedAmount);

/// <summary>
/// One difference between two FRN snapshots. <see cref="Kind"/> is the portfolio label.
/// </summary>
public sealed record FrnFieldChange(
    string FundingRequestNumber,
    string ApplicationNumber,
    string Name,
    string Kind,
    string Before,
    string After);

/// <summary>
/// Compares FRN Status rows already mapped by <see cref="FrnCommitment"/>.
/// </summary>
public static class FrnSnapshot
{
    public const string StatusKind = FrnCommitment.StatusKind;

    public const string ReportedCommittedAmountKind = FrnCommitment.ReportedCommittedAmountKind;

    public const string FcdlDateKind = FrnCommitment.FcdlDateKind;

    public const string RevisedFcdlDateKind = FrnCommitment.RevisedFcdlDateKind;

    public const string DisbursedAmountKind = FrnCommitment.DisbursedAmountKind;

    public const string NewKind = "New FRN";

    public const string MissingKind = "FRN no longer returned by this query";

    public const string ReportedOutcome = "FrnReported";

    public const string ObservationNote = "This row is an FRN status observation from USAC Open Data, not a filed application.";

    /// <summary>
    /// Shown with portfolio change text. It does not change export <c>CommittedAmount</c>.
    /// </summary>
    public const string ReportedAmountAssumption = FrnCommitment.ReportedAmountAssumption;

    public static readonly string[] KindOrder =
    [
        StatusKind,
        ReportedCommittedAmountKind,
        FcdlDateKind,
        RevisedFcdlDateKind,
        DisbursedAmountKind,
        NewKind,
        MissingKind
    ];

    public static string Blank(string? value) => string.IsNullOrWhiteSpace(value) ? "" : value.Trim();

    /// <summary>
    /// Maps one Open Data row with <see cref="FrnCommitment.FromPublished"/>.
    /// The reported amount keeps that method's Funded-only rule.
    /// </summary>
    public static ReportedFrn FromPublishedRow(UsacRow row)
    {
        var commitment = FrnCommitment.FromPublished(
            row.Status,
            row.FundingCommitmentRequest,
            row.FcdlLetterDate,
            row.RevisedFcdlDate,
            row.TotalAuthorizedDisbursement);
        return new ReportedFrn(
            Blank(row.FundingRequestNumber),
            Blank(row.Number),
            Blank(row.Name),
            commitment.FrnStatus,
            commitment.CommittedAmount,
            commitment.FcdlDate,
            commitment.RevisedFcdlDate,
            commitment.DisbursedAmount);
    }

    /// <summary>
    /// Builds a snapshot row through the same mapping as an export.
    /// Null published fields stay blank. They are not written as zero.
    /// </summary>
    public static ReportedFrn Create(
        string fundingRequestNumber,
        string applicationNumber = "",
        string name = "",
        string? frnStatus = "",
        string? fundingCommitmentRequest = "",
        string? fcdlLetterDate = "",
        string? revisedFcdlDate = "",
        string? totalAuthorizedDisbursement = "")
    {
        var commitment = FrnCommitment.FromPublished(
            frnStatus,
            fundingCommitmentRequest,
            fcdlLetterDate,
            revisedFcdlDate,
            totalAuthorizedDisbursement);
        return new ReportedFrn(
            Blank(fundingRequestNumber),
            Blank(applicationNumber),
            Blank(name),
            commitment.FrnStatus,
            commitment.CommittedAmount,
            commitment.FcdlDate,
            commitment.RevisedFcdlDate,
            commitment.DisbursedAmount);
    }

    /// <summary>
    /// Keeps rows that have a funding request number. A blank number is not an FRN identity.
    /// A non-Current <c>form_version</c>, a BEN that does not match, or a duplicate number
    /// rejects the whole result so a partial snapshot is not saved.
    /// </summary>
    public static List<ReportedFrn> FromRows(string ben, IEnumerable<UsacRow> rows)
    {
        var parsed = new List<ReportedFrn>();
        foreach (var row in rows)
        {
            if (!string.IsNullOrEmpty(row.Version) && !string.Equals(row.Version, "Current", StringComparison.Ordinal))
            {
                throw new InvalidOperationException("FRN status rows included a version other than Current.");
            }

            var frn = FromPublishedRow(row);
            if (frn.FundingRequestNumber.Length == 0)
            {
                continue;
            }

            if (!string.IsNullOrEmpty(row.Ben) && !string.Equals(Blank(row.Ben), Blank(ben), StringComparison.Ordinal))
            {
                throw new InvalidOperationException("The returned FRNs do not match the requested BEN.");
            }

            parsed.Add(frn);
        }

        Index(parsed);
        return parsed.OrderBy(frn => frn.FundingRequestNumber, StringComparer.Ordinal).ToList();
    }

    public static List<FrnFieldChange> Compare(IEnumerable<ReportedFrn> before, IEnumerable<ReportedFrn> after)
    {
        var oldRows = Index(before);
        var newRows = Index(after);
        var changes = new List<FrnFieldChange>();
        foreach (var (id, row) in newRows)
        {
            if (!oldRows.TryGetValue(id, out var previous))
            {
                changes.Add(new FrnFieldChange(id, Blank(row.ApplicationNumber), Blank(row.Name), NewKind, "", Describe(row)));
                continue;
            }

            var beforeCommitment = new FrnCommitment(previous.FrnStatus, previous.ReportedCommittedAmount, previous.FcdlDate, previous.RevisedFcdlDate, previous.DisbursedAmount);
            var afterCommitment = new FrnCommitment(row.FrnStatus, row.ReportedCommittedAmount, row.FcdlDate, row.RevisedFcdlDate, row.DisbursedAmount);
            foreach (var diff in FrnCommitment.Compare(beforeCommitment, afterCommitment))
                changes.Add(new FrnFieldChange(row.FundingRequestNumber, Blank(row.ApplicationNumber), Blank(row.Name), diff.Kind, diff.Before, diff.After));
        }

        foreach (var (id, row) in oldRows)
        {
            if (!newRows.ContainsKey(id))
            {
                changes.Add(new FrnFieldChange(id, Blank(row.ApplicationNumber), Blank(row.Name), MissingKind, Describe(row), ""));
            }
        }

        return changes
            .OrderBy(change => change.FundingRequestNumber, StringComparer.Ordinal)
            .ThenBy(change => KindIndex(change.Kind))
            .ToList();
    }

    public static string Describe(ReportedFrn frn) =>
        "status " + Display(frn.FrnStatus)
        + "; reported committed amount " + Display(frn.ReportedCommittedAmount)
        + "; FCDL date " + Display(frn.FcdlDate)
        + "; revised FCDL date " + Display(frn.RevisedFcdlDate)
        + "; disbursed amount " + Display(frn.DisbursedAmount);

    static Dictionary<string, ReportedFrn> Index(IEnumerable<ReportedFrn> rows)
    {
        var map = new Dictionary<string, ReportedFrn>(StringComparer.Ordinal);
        foreach (var row in rows)
        {
            var id = Blank(row.FundingRequestNumber);
            if (id.Length == 0 || !map.TryAdd(id, row))
            {
                throw new InvalidOperationException("The returned FRNs do not form a valid complete snapshot.");
            }
        }

        return map;
    }

    static string Display(string? value)
    {
        var text = Blank(value);
        return text.Length == 0 ? "blank" : text;
    }

    static int KindIndex(string kind)
    {
        var index = Array.IndexOf(KindOrder, kind);
        return index < 0 ? KindOrder.Length : index;
    }
}
