using System.Globalization;
using System.Text.Json.Serialization;

namespace ERateLookup.Core;

/// <summary>
/// Shared lookup export columns. The command-line table and CSV, and the app
/// lookup CSV and Excel files, use <see cref="BaseColumns"/>. Optional groups
/// stay out of that list; pass <see cref="ExportColumns"/> to include them.
/// Category 2 budget columns are the budget group only.
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
    /// App headers renamed so CSV and Excel match <see cref="BaseColumns"/>.
    /// The key is the header written before this change. The value is the header written now.
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
        int? form471Count) =>
        new()
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
        };

    /// <summary>
    /// Table and CSV show the queried BEN when the published row did not include one.
    /// JSON keeps the published value.
    /// </summary>
    public static ExportRow WithDisplayedBen(ExportRow row, string? queryBen) =>
        row with
        {
            Ben = string.IsNullOrEmpty(row.Ben) ? queryBen ?? "" : row.Ben
        };

    /// <summary>
    /// CamelCase JSON row used by the command-line <c>rows</c> array.
    /// Portfolio-only properties stay null here, so a lookup document omits them.
    /// </summary>
    public static ExportJsonRow ToJsonRow(ExportRow row) =>
        new(
            int.Parse(row.Form, CultureInfo.InvariantCulture),
            row.ApplicationNumber,
            row.FundingRequestNumber,
            row.Status,
            row.CertifiedDate,
            row.Nickname,
            row.Ben,
            row.Name,
            row.Services,
            row.Pdf,
            row.RequestedAmount,
            row.PreDiscountAmount,
            row.Source);

    /// <summary>
    /// Same JSON row as <see cref="ToJsonRow"/>, plus the four portfolio fields.
    /// Empty portfolio fields are written as empty strings.
    /// </summary>
    public static ExportJsonRow ToPortfolioJsonRow(ExportRow row) =>
        ToJsonRow(row) with
        {
            LookupOutcome = row.LookupOutcome,
            Change = row.Change,
            SourceAndNotes = row.SourceAndNotes,
            AllowableContractDate = row.AllowableContractDate,
        };

    static string CountText(int? count) => count?.ToString(CultureInfo.InvariantCulture) ?? "";

    static IEnumerable<Column> Selected(ExportColumns columns) =>
        Definitions.Where(column => column.Group switch
        {
            ExportColumnGroup.Base => true,
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

    public int? FundingYear
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

/// <summary>
/// JSON row in the command-line camelCase shape. <c>form</c> is a number.
/// The four portfolio properties are omitted when null.
/// </summary>
public sealed record ExportJsonRow(
    int Form,
    string ApplicationNumber,
    string FundingRequestNumber,
    string Status,
    string CertifiedDate,
    string Nickname,
    string Ben,
    string Name,
    string Services,
    string Pdf,
    string RequestedAmount,
    string PreDiscountAmount,
    string Source)
{
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? LookupOutcome
    {
        get; init;
    }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Change
    {
        get; init;
    }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? SourceAndNotes
    {
        get; init;
    }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? AllowableContractDate
    {
        get; init;
    }
}

/// <summary>
/// One lookup or portfolio export, ready for CSV, Excel, or JSON.
/// </summary>
public sealed class PreparedExport
{
    public required List<string[]> Table
    {
        get; init;
    }

    public required ExportMetadata Metadata
    {
        get; init;
    }

    public required IReadOnlyList<ExportJsonRow> JsonRows
    {
        get; init;
    }

    public void Write(string path, CancellationToken cancellationToken = default) =>
        DataExporter.WritePrepared(path, Table, Metadata, JsonRows, cancellationToken);
}
