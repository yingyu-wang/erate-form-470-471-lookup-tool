using System.Globalization;
using System.Text;

namespace ERate470Lookup.Core;

public static class PortfolioInsights
{
    public const string Form470Source="https://opendata.usac.org/d/jt8s-3q52";
    public static string ContractDate(Filing? filing)
    {
        var raw=filing?.AllowableContractDate??"";
        return raw.Length>=10&&DateOnly.TryParseExact(raw[..10],"yyyy-MM-dd",CultureInfo.InvariantCulture,DateTimeStyles.None,out var date)
            ?date.ToString("yyyy-MM-dd",CultureInfo.InvariantCulture):"Unknown";
    }
    public static string Summary(PortfolioSession session,CaliforniaDirectory directory)
    {
        var text=new StringBuilder(session.HistorySummary());
        foreach(var group in session.Rows.Where(r=>r.Query.Form==FilingForm.Form471).GroupBy(r=>(r.Query.Ben,r.Query.Year)))
            text.AppendLine($"\nBEN {group.Key.Ben} · FY{group.Key.Year} · observed Form 471 requested funding: {RequestedTotal(group)} USD.");
        text.AppendLine("\n\nC2 budget snapshots (pre-discount; committed is not disbursed):");
        foreach(var key in session.Rows.Select(r=>(r.Query.Ben,r.Query.Year)).Distinct())
        {
            var b=directory.GetBudget(key.Ben,key.Year);
            text.AppendLine($"BEN {key.Ben}, FY{key.Year}: "+(b is null?"Budget not reported in the California snapshot. This is not zero.":
                $"{b.Name} · {b.Cycle} · {b.Version}\nTotal {BudgetRow.Money(b.Total)}; committed {BudgetRow.Money(b.Committed)}; pending {BudgetRow.Money(b.Pending)}; reported available {BudgetRow.Money(b.Available)}."));
        }
        text.AppendLine("Budget snapshot: "+directory.LastUpdated);
        text.AppendLine("Provider comparisons are unavailable in this application-level view. Requested funding sums observed application totals; it is not committed or disbursed funding.");
        return text.ToString();
    }
    public static string RequestedTotal(IEnumerable<PortfolioRow> source)
    {
        var rows=source.ToList();
        if(rows.Count==0||rows.Any(r=>r.Outcome is "Failed" or "Cancelled" or "Not started" or "Not saved"))return "Unknown";
        var total=0m;
        foreach(var row in rows.Where(r=>r.Outcome=="Success").DistinctBy(r=>r.Id))
        {
            if(!decimal.TryParse(row.Filing?.RequestedAmount,NumberStyles.Number,CultureInfo.InvariantCulture,out var amount)||amount<0)return "Unknown";
            total+=amount;
        }
        return total.ToString("N2",CultureInfo.InvariantCulture);
    }
    public static async Task<string> DeadlinesAsync(PortfolioSession session,FilingWindow windows,CancellationToken ct)
    {
        var text=new StringBuilder("Source-reported dates; verify current EPC/RFP amendments before acting. Date-only values have no asserted time or timezone.\n\n");
        foreach(var r in session.Rows.Where(r=>r.Query.Form==FilingForm.Form470&&r.Outcome=="Success").OrderBy(r=>ContractDate(r.Filing)))
            text.AppendLine($"BEN {r.Query.Ben} · FY{r.Query.Year} · Form 470 {r.Filing?.Number}\nAllowable contract date: {ContractDate(r.Filing)}\nObserved {r.Observed:O} · {Form470Source}\n");
        foreach(var year in session.Rows.Select(r=>r.Query.Year).Distinct().Order())
        {
            ct.ThrowIfCancellationRequested();
            try
            {
                var window=await windows.GetAsync(year,ct);
                text.AppendLine($"FY{year} Form 471 window: {window.Opens?.ToString("yyyy-MM-dd")??"Unknown"} to {window.Closes?.ToString("yyyy-MM-dd")??"Unknown"}\nSource checked {window.CheckedAt:O}\n{window.Source}\n");
            }
            catch(Exception ex) when(ex is HttpRequestException or IOException or System.Text.Json.JsonException)
            {text.AppendLine($"FY{year} filing window: Unknown (source unavailable). {FilingWindow.Announcements}");}
        }
        return text.ToString();
    }
}
