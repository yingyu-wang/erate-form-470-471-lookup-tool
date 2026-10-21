using System.Globalization;

namespace ERateLookup.Core;

/// <summary>
/// Shared lookup export columns. <see cref="BaseColumns"/> is the shared list.
/// The command-line table and CSV use <see cref="CliColumns"/>, which appends
/// the FRN commitment columns. Other groups stay out until
/// <see cref="ExportColumns"/> selects them.
/// App and portfolio files still write their current headers.
/// </summary>
public static class ExportSchema
{
    static readonly Column[] Definitions =
    [
        new("ExportedUTC", ExportColumnGroup.LookupTimestamp, static row => row.ExportedUtc),
        new("LookupFetchedUTC", ExportColumnGroup.LookupTimestamp, static row => row.LookupFetchedUtc),
        new("Form", ExportColumnGroup.Base, static row => row.Form),
        new("FundingYear", ExportColumnGroup.Base, static row => row.FundingYear),
        new("BEN", ExportColumnGroup.Base, static row => row.Ben),
        new("Name", ExportColumnGroup.Base, static row => row.Name),
        new("ApplicationNumber", ExportColumnGroup.Base, static row => row.ApplicationNumber),
        new("FundingRequestNumber", ExportColumnGroup.Base, static row => row.FundingRequestNumber),
        new("Status", ExportColumnGroup.Base, static row => row.Status),
        new("CertifiedDate", ExportColumnGroup.Base, static row => row.CertifiedDate),
        new("Nickname", ExportColumnGroup.Base, static row => row.Nickname),
        new("Services", ExportColumnGroup.Base, static row => row.Services),
        new("RequestedAmount", ExportColumnGroup.Base, static row => row.RequestedAmount),
        new("PreDiscountAmount", ExportColumnGroup.Base, static row => row.PreDiscountAmount),
        new("PDF", ExportColumnGroup.Base, static row => row.Pdf),
        new("Source", ExportColumnGroup.Base, static row => row.Source),
        new("Linked", ExportColumnGroup.Base, static row => row.Linked),
        new("Form470Count", ExportColumnGroup.Base, static row => row.Form470Count),
        new("Form471Count", ExportColumnGroup.Base, static row => row.Form471Count),
        new("FrnStatus", ExportColumnGroup.FrnCommitment, static row => row.FrnStatus),
        new("CommittedAmount", ExportColumnGroup.FrnCommitment, static row => row.CommittedAmount),
        new("FcdlDate", ExportColumnGroup.FrnCommitment, static row => row.FcdlDate),
        new("RevisedFcdlDate", ExportColumnGroup.FrnCommitment, static row => row.RevisedFcdlDate),
        new("DisbursedAmount", ExportColumnGroup.FrnCommitment, static row => row.DisbursedAmount),
        new("LookupResult", ExportColumnGroup.LookupContext, static row => row.LookupResult),
        new("DirectoryUpdatedUTC", ExportColumnGroup.LookupContext, static row => row.DirectoryUpdatedUtc),
        new("BudgetCycle", ExportColumnGroup.Budget, static row => row.BudgetCycle),
        new("USACBudgetStatus", ExportColumnGroup.Budget, static row => row.UsacBudgetStatus),
        new("BudgetDisplayStatus", ExportColumnGroup.Budget, static row => row.BudgetDisplayStatus),
        new("TotalC2BudgetPreDiscount", ExportColumnGroup.Budget, static row => row.TotalC2BudgetPreDiscount),
        new("CommittedC2BudgetPreDiscount", ExportColumnGroup.Budget, static row => row.CommittedC2BudgetPreDiscount),
        new("PendingC2BudgetPreDiscount", ExportColumnGroup.Budget, static row => row.PendingC2BudgetPreDiscount),
        new("ReportedRemainingC2BudgetPreDiscount", ExportColumnGroup.Budget, static row => row.ReportedRemainingC2BudgetPreDiscount),
        new("LookupOutcome", ExportColumnGroup.Portfolio, static row => row.LookupOutcome),
        new("Change", ExportColumnGroup.Portfolio, static row => row.Change),
        new("SourceAndNotes", ExportColumnGroup.Portfolio, static row => row.SourceAndNotes),
        new("AllowableContractDate", ExportColumnGroup.Portfolio, static row => row.AllowableContractDate),
    ];

    /// <summary>
    /// Current app and portfolio headers that correspond to a unified name.
    /// Nothing reads this while those exports keep their current headers.
    /// </summary>
    public static IReadOnlyDictionary<string, string> PendingHeaderRenames
    {
        get;
    } =
        new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["Customer"] = "Name",
            ["FormType"] = "Form",
            ["FilingStatus"] = "Status",
            ["RequestedFundingUSD"] = "RequestedAmount",
            ["PreDiscountEligibleUSD"] = "PreDiscountAmount",
        };

    public static IReadOnlyList<string> BaseColumns => Columns();

    /// <summary>
    /// Command-line table and CSV: the shared base plus the five FRN columns.
    /// Those five names stay out of <see cref="BaseColumns"/>.
    /// </summary>
    public static ExportColumns CliColumns { get; } = new() { IncludeFrnCommitment = true };

    public static IReadOnlyList<string> Names(ExportColumnGroup group) =>
        Definitions.Where(column => column.Group == group).Select(column => column.Name).ToArray();

    public static IReadOnlyList<string> Columns(ExportColumns columns = default) =>
        Selected(columns).Select(column => column.Name).ToArray();

    public static string[] ToRow(ExportRow row, ExportColumns columns = default) =>
        Selected(columns).Select(column => column.Value(row)).ToArray();

    public static ExportRow FromFiling(
        FilingForm form,
        Filing filing,
        int fundingYear,
        bool linked,
        int? form470Count,
        int? form471Count) =>
        new()
        {
            Form = ((int)form).ToString(CultureInfo.InvariantCulture),
            FundingYear = fundingYear.ToString(CultureInfo.InvariantCulture),
            Ben = filing.Ben,
            Name = filing.Name,
            ApplicationNumber = filing.Number,
            FundingRequestNumber = "",
            Status = filing.Status,
            CertifiedDate = filing.Certified,
            Nickname = filing.Nickname,
            Services = filing.Services,
            RequestedAmount = filing.RequestedAmount,
            PreDiscountAmount = filing.PreDiscountAmount,
            Pdf = filing.Pdf,
            Source = UsacSources.Application(form, filing.Number),
            Linked = linked ? "true" : "false",
            Form470Count = CountText(form470Count),
            Form471Count = CountText(form471Count),
        };

    public static ExportRow FromFundingRequest(
        UsacRow row,
        int fundingYear,
        bool linked,
        int? form470Count,
        int? form471Count)
    {
        // [Inference] CommittedAmount copies funding_commitment_request only when
        // the published FRN status is Funded. Q8 (FCDL confirmation) is pending.
        var commitment = FrnCommitment.FromPublished(
            row.Status,
            row.FundingCommitmentRequest,
            row.FcdlLetterDate,
            row.RevisedFcdlDate,
            row.TotalAuthorizedDisbursement);
        return new()
        {
            Form = ((int)FilingForm.Form471).ToString(CultureInfo.InvariantCulture),
            FundingYear = fundingYear.ToString(CultureInfo.InvariantCulture),
            Ben = row.Ben,
            Name = row.Name,
            ApplicationNumber = row.Number,
            FundingRequestNumber = row.FundingRequestNumber,
            Status = string.IsNullOrWhiteSpace(row.Status) ? "Unknown" : row.Status,
            CertifiedDate = UsacTimestamps.CalendarDate(row.Certified),
            Nickname = row.Nickname,
            Services = row.Service,
            RequestedAmount = row.RequestedAmount,
            PreDiscountAmount = row.PreDiscountAmount,
            Pdf = row.Pdf?.Url ?? row.PdfUrl,
            Source = UsacSources.FundingRequest(row.FundingRequestNumber, row.Number),
            Linked = linked ? "true" : "false",
            Form470Count = CountText(form470Count),
            Form471Count = CountText(form471Count),
            FrnStatus = commitment.FrnStatus,
            CommittedAmount = commitment.CommittedAmount,
            FcdlDate = commitment.FcdlDate,
            RevisedFcdlDate = commitment.RevisedFcdlDate,
            DisbursedAmount = commitment.DisbursedAmount,
        };
    }

    /// <summary>
    /// Table and CSV show the queried BEN when the published row did not include one.
    /// JSON keeps the published value.
    /// </summary>
    public static ExportRow WithDisplayedBen(ExportRow row, string? queryBen) =>
        row with
        {
            Ben = string.IsNullOrEmpty(row.Ben) ? queryBen ?? "" : row.Ben
        };

    static string CountText(int? count) => count?.ToString(CultureInfo.InvariantCulture) ?? "";

    static IEnumerable<Column> Selected(ExportColumns columns) =>
        Definitions.Where(column => column.Group switch
        {
            ExportColumnGroup.Base => true,
            ExportColumnGroup.FrnCommitment => columns.IncludeFrnCommitment,
            ExportColumnGroup.LookupTimestamp => columns.IncludeLookupTimestamps,
            ExportColumnGroup.LookupContext => columns.IncludeLookupContext,
            ExportColumnGroup.Budget => columns.IncludeBudget,
            ExportColumnGroup.Portfolio => columns.IncludePortfolio,
            _ => false
        });

    readonly record struct Column(string Name, ExportColumnGroup Group, Func<ExportRow, string> Value);
}

public enum ExportColumnGroup
{
    Base,
    FrnCommitment,
    LookupTimestamp,
    LookupContext,
    Budget,
    Portfolio
}

/// <summary>
/// Which optional column groups to append. The default is the shared base only.
/// </summary>
public readonly record struct ExportColumns
{
    public bool IncludeLookupTimestamps
    {
        get; init;
    }

    public bool IncludeLookupContext
    {
        get; init;
    }

    public bool IncludeBudget
    {
        get; init;
    }

    public bool IncludePortfolio
    {
        get; init;
    }

    public bool IncludeFrnCommitment
    {
        get; init;
    }
}

public sealed record ExportRow
{
    public string Form { get; init; } = "";

    public string FundingYear { get; init; } = "";

    public string Ben { get; init; } = "";

    public string Name { get; init; } = "";

    public string ApplicationNumber { get; init; } = "";

    public string FundingRequestNumber { get; init; } = "";

    public string Status { get; init; } = "";

    public string CertifiedDate { get; init; } = "";

    public string Nickname { get; init; } = "";

    public string Services { get; init; } = "";

    public string RequestedAmount { get; init; } = "";

    public string PreDiscountAmount { get; init; } = "";

    public string Pdf { get; init; } = "";

    public string Source { get; init; } = "";

    public string Linked { get; init; } = "";

    public string Form470Count { get; init; } = "";

    public string Form471Count { get; init; } = "";

    public string FrnStatus { get; init; } = "";

    public string CommittedAmount { get; init; } = "";

    public string FcdlDate { get; init; } = "";

    public string RevisedFcdlDate { get; init; } = "";

    public string DisbursedAmount { get; init; } = "";

    public string ExportedUtc { get; init; } = "";

    public string LookupFetchedUtc { get; init; } = "";

    public string LookupResult { get; init; } = "";

    public string DirectoryUpdatedUtc { get; init; } = "";

    public string BudgetCycle { get; init; } = "";

    public string UsacBudgetStatus { get; init; } = "";

    public string BudgetDisplayStatus { get; init; } = "";

    public string TotalC2BudgetPreDiscount { get; init; } = "";

    public string CommittedC2BudgetPreDiscount { get; init; } = "";

    public string PendingC2BudgetPreDiscount { get; init; } = "";

    public string ReportedRemainingC2BudgetPreDiscount { get; init; } = "";

    public string LookupOutcome { get; init; } = "";

    public string Change { get; init; } = "";

    public string SourceAndNotes { get; init; } = "";

    public string AllowableContractDate { get; init; } = "";
}

/// <summary>
/// JSON envelope around export rows. Null link counts are left out of the document.
/// </summary>
public sealed class ExportMetadata
{
    public int? Form
    {
        get; init;
    }

    public bool Linked
    {
        get; init;
    }

    public int? Form470Count
    {
        get; init;
    }

    public int? Form471Count
    {
        get; init;
    }

    public string? Ben
    {
        get; init;
    }

    public string? ApplicantName
    {
        get; init;
    }

    public int FundingYear
    {
        get; init;
    }

    public string? ApplicationNumber
    {
        get; init;
    }

    public string? FundingRequestNumber
    {
        get; init;
    }

    public required DateTimeOffset FetchedAt
    {
        get; init;
    }
}
