using System.Globalization;

namespace ERateLookup.Core;

public enum LookupSurface
{
    Windows,
    Avalonia,
    Android
}

public sealed record NameSearchView(IReadOnlyList<Entity> Matches, bool Visible, string? Status);

public sealed record FilingLookupView(
    string Ben,
    string Name,
    int Year,
    FilingForm Form,
    IReadOnlyList<Filing> Filings,
    DateTimeOffset FetchedAt,
    bool FromCache,
    bool Complete,
    bool? HasNewCycleForm470,
    string Status,
    string BudgetText);

public sealed record WindowBannerView(string Text, string? SourceUrl, bool PromptToOpen);

public sealed class LookupViewModel
{
    readonly UsacClient client;
    readonly CaliforniaDirectory directory;
    readonly FilingWindow windows;
    readonly LookupSurface surface;
    readonly Func<DateTimeOffset> clock;

    public LookupViewModel(UsacClient client, CaliforniaDirectory directory, FilingWindow windows, LookupSurface surface, Func<DateTimeOffset>? clock = null)
    {
        this.client = client;
        this.directory = directory;
        this.windows = windows;
        this.surface = surface;
        this.clock = clock ?? (() => DateTimeOffset.UtcNow);
    }

    public (int Current, int Next) NewCycleYears => client.NewCycleFundingYears();

    public static bool IsBen(string text) => text.All(char.IsAsciiDigit);

    public string? ValidateCustomerQuery(string? text, string? state)
    {
        text = text?.Trim() ?? "";
        state = state?.Trim() ?? "";
        if (text.Length == 0 || (!IsBen(text) && text.Length < 2))
            return "Enter a BEN or at least two characters of a customer name.";
        if (state.Length != 0 && (state.Length != 2 || !state.All(char.IsAsciiLetter)))
            return "Use a two-letter state abbreviation.";
        return null;
    }

    public NameSearchView Suggest(string? text)
    {
        text = text?.Trim() ?? "";
        if (text.Length < 2 || IsBen(text))
            return new NameSearchView([], false, null);
        return SearchDirectory(text, typed: true);
    }

    public NameSearchView SearchByName(string text) => SearchDirectory(text.Trim(), typed: false);

    NameSearchView SearchDirectory(string text, bool typed)
    {
        try
        {
            var found = directory.Search(text);
            return new NameSearchView(found, found.Count > 0, MatchStatus(found.Count, typed));
        }
        catch (Exception ex) when (AppExceptions.IsOperational(ex))
        {
            AppLog.Error(ex, "Local directory search");
            var prefix = surface == LookupSurface.Windows ? "Local directory search failed: " : "Local search failed: ";
            return new NameSearchView([], false, prefix + ex.Message);
        }
    }

    string MatchStatus(int count, bool typed)
    {
        if (surface == LookupSurface.Windows)
        {
            if (!typed)
            {
                return count == 0
                    ? "No local California name match. Try a shorter name, refresh the directory, or enter the filing BEN directly."
                    : "Select one or more districts to save, or select one customer to look up filings." + (count == 15 ? " Showing up to 15 matches; type more letters to narrow the list." : "");
            }

            return count == 0
                ? "No California name match. Try another spelling or enter a filing BEN."
                : $"{count} local match(es). Select districts to save, or one customer to look up." + (count == 15 ? " Type more letters to narrow the list." : "");
        }

        if (!typed)
        {
            return count == 0
                ? "No local California match. Try a shorter name or enter a BEN."
                : "Select districts to save, or one customer to look up filings.";
        }

        return count == 0
            ? "No local match. Enter the filing BEN or try another spelling."
            : $"{count} local match(es). Select districts to save, or one customer to look up." + (count == 15 ? " Type more letters to narrow the list." : "");
    }

    public async Task<FilingLookupView> LookupAsync(string ben, int year, FilingForm form, bool refresh, string name, CancellationToken cancellationToken)
    {
        var response = await client.GetFilingsAsync(ben, year, form, refresh, cancellationToken).ConfigureAwait(false);
        bool? hasCycle = null;
        var notes = "";
        if (ChecksNewCycle(year))
        {
            if (surface == LookupSurface.Android)
            {
                var optional = await AndroidBehavior.OptionalAsync(
                    async token => (await client.HasNewCycleForm470Async(ben, refresh, token).ConfigureAwait(false)).Data,
                    cancellationToken).ConfigureAwait(false);
                if (optional.Available)
                    hasCycle = optional.Value;
                else
                    notes += " Budget eligibility cross-check unavailable.";
            }
            else
            {
                try
                {
                    hasCycle = (await client.HasNewCycleForm470Async(ben, refresh, cancellationToken).ConfigureAwait(false)).Data;
                }
                catch (OperationCanceledException ex) when (!cancellationToken.IsCancellationRequested)
                {
                    AppLog.Error(ex, "New-cycle Form 470 check");
                    notes = surface == LookupSurface.Windows
                        ? " New-cycle filing check timed out; budget status comes from USAC."
                        : " New-cycle check unavailable; budget status comes from USAC.";
                }
                catch (Exception ex) when (AppExceptions.IsOperational(ex))
                {
                    AppLog.Error(ex, "New-cycle Form 470 check");
                    notes = surface == LookupSurface.Windows
                        ? " New-cycle filing check failed; budget status comes from USAC."
                        : " New-cycle check unavailable; budget status comes from USAC.";
                }
            }
        }

        if (surface == LookupSurface.Android && form == FilingForm.Form471)
        {
            var optional = await AndroidBehavior.OptionalAsync(token => windows.GetAsync(year, token), cancellationToken).ConfigureAwait(false);
            notes += optional.Available
                ? "\n" + optional.Value!.Message(clock())
                : " Filing-window check unavailable; check USAC’s announcement. Published filings are still shown.";
        }

        var filings = response.Data;
        return new FilingLookupView(
            ben,
            name,
            year,
            form,
            filings,
            response.FetchedAt,
            response.FromCache,
            true,
            hasCycle,
            StatusText(ben, year, form, filings.Count, response.FromCache, response.FetchedAt, notes),
            BudgetText(ben, year, hasCycle));
    }

    bool ChecksNewCycle(int year) =>
        surface == LookupSurface.Android ? year is >= 2026 and <= 2030 : year >= 2026;

    string StatusText(string ben, int year, FilingForm form, int count, bool fromCache, DateTimeOffset fetched, string notes)
    {
        if (surface == LookupSurface.Android)
        {
            var headline = count == 0
                ? "No certified filings found in published data; this does not prove no filing exists."
                : $"{count} published Form {(int)form} applications.";
            var source = fromCache ? "Cached" : "Live";
            return headline + $" BEN {ben}, FY{year}. {source}; fetched {fetched.LocalDateTime:g}." + notes;
        }

        var filed = count > 0
            ? $"FILED — {count} certified Form {(int)form} application(s)"
            : $"No certified Form {(int)form} found in currently published data";
        var live = fromCache ? "Cached" : "Live query";
        var yearLabel = surface == LookupSurface.Windows ? $"FY {year}" : $"FY{year}";
        return filed + $" for BEN {ben}, {yearLabel}. {live}; fetched {fetched.LocalDateTime:g}." + notes;
    }

    public string BudgetText(string ben, int year, bool? hasNewCycleForm470)
    {
        var row = directory.GetBudget(ben, year);
        var (current, next) = NewCycleYears;
        var years = $"{current} or {next}";
        if (surface == LookupSurface.Android)
            return AndroidBudget(row, hasNewCycleForm470, years);
        if (surface == LookupSurface.Avalonia)
            return AvaloniaBudget(ben, row, hasNewCycleForm470, years);
        return WindowsBudget(ben, year, row, hasNewCycleForm470, years, directory.LastUpdated);
    }

    static string WindowsBudget(string ben, int year, BudgetRow? row, bool? hasCycle, string years, string updated)
    {
        var cycle = year >= 2026 ? "FY2026–2030" : year >= 2021 ? "FY2021–2025" : "not loaded for this year";
        if (row is null)
            return $"C2 budget · {cycle} · BEN {ben}\nNot reported in the downloaded C2 dataset. This is not a zero budget and does not establish whether an application was filed.\nDirectory updated: {updated}";
        var forecast = BudgetRow.ForecastView(row.Version, hasCycle);
        var heading = forecast ? "Forecast C2 budget" : "C2 budget";
        var remaining = forecast ? "Forecast remaining (not confirmed)" : "Reported available";
        var cycleNote = hasCycle == false
            ? forecast
                ? $"No certified Form 470 found for {years}; forecast view. "
                : $"No certified Form 470 found for {years}; USAC Confirmed status is preserved. "
            : "";
        var balanceNote = Negative(row.Available) ? " Negative reported remaining balance — review with USAC; not clamped to zero." : "";
        return $"{heading} · {row.Cycle} · USAC status: {row.Version} · {row.Name}\nTotal: {BudgetRow.Money(row.Total)}     Committed: {BudgetRow.Money(row.Committed)}     Pending: {BudgetRow.Money(row.Pending)}\n{remaining}: {BudgetRow.Money(row.Available)}\n{cycleNote}Pre-discount figures. Posting Form 470 does not confirm a budget; C2 Form 471 review determines confirmation. Updated: {updated}{balanceNote}";
    }

    string AvaloniaBudget(string ben, BudgetRow? row, bool? hasCycle, string years)
    {
        if (row is null)
            return $"C2 budget · BEN {ben}\nNot reported in the downloaded budget data. This is not a zero budget.\nDirectory updated: {directory.LastUpdated}";
        var forecast = BudgetRow.ForecastView(row.Version, hasCycle);
        var note = hasCycle == false ? $"No certified Form 470 found for {years}. " : "";
        if (Negative(row.Available))
            note += "Negative remaining balance: review with USAC. ";
        return $"{(forecast ? "Forecast C2 budget" : "C2 budget")} · {row.Cycle} · USAC status: {row.Version} · {row.Name}\nTotal: {BudgetRow.Money(row.Total)}     Committed: {BudgetRow.Money(row.Committed)}     Pending: {BudgetRow.Money(row.Pending)}\n{(forecast ? "Forecast remaining (not confirmed)" : "Reported available")}: {BudgetRow.Money(row.Available)}\n{note}Pre-discount figures; committed is not disbursed. Form 470 does not confirm a budget. Updated: {directory.LastUpdated}";
    }

    string AndroidBudget(BudgetRow? row, bool? hasCycle, string years)
    {
        if (row is null)
            return "C2 budget not reported in the CA snapshot. This is not a zero budget. Snapshot: " + directory.LastUpdated;
        var forecast = BudgetRow.ForecastView(row.Version, hasCycle);
        var text = $"{(forecast ? "Forecast C2 budget" : "C2 budget")} · {row.Cycle}\n{row.Name}\nUSAC status: {row.Version}\nTotal: {BudgetRow.Money(row.Total)}\nCommitted: {BudgetRow.Money(row.Committed)}\nPending: {BudgetRow.Money(row.Pending)}\n{(forecast ? "Forecast remaining (not confirmed)" : "Reported available")}: {BudgetRow.Money(row.Available)}\nPre-discount figures. Committed is not disbursed. Form 470 does not confirm a budget.\nSnapshot: {directory.LastUpdated}";
        if (hasCycle == false)
            text += $"\nNo certified Form 470 found for {years}.";
        if (Negative(row.Available))
            text += "\nNegative remaining balance: review with USAC.";
        return text;
    }

    static bool Negative(string? available) =>
        decimal.TryParse(available, NumberStyles.Number, CultureInfo.InvariantCulture, out var balance) && balance < 0;

    public async Task<WindowBannerView> WindowBannerAsync(int year, FilingForm form, CancellationToken cancellationToken)
    {
        if (form != FilingForm.Form471)
            return new WindowBannerView("", null, false);
        try
        {
            var schedule = await windows.GetAsync(year, cancellationToken).ConfigureAwait(false);
            return new WindowBannerView(schedule.Message(clock()), schedule.Source, true);
        }
        catch (OperationCanceledException ex) when (cancellationToken.IsCancellationRequested)
        {
            AppLog.Error(ex, "Form 471 window check");
            return new WindowBannerView("", null, false);
        }
        catch (OperationCanceledException ex)
        {
            AppLog.Error(ex, "Form 471 window check");
            var text = surface == LookupSurface.Windows
                ? "Window check timed out. Check USAC's official announcements; filing lookup remains available."
                : "Could not verify filing-window dates. Check USAC's official announcements; lookups remain available.";
            return new WindowBannerView(text, null, false);
        }
        catch (Exception ex) when (AppExceptions.IsOperational(ex))
        {
            AppLog.Error(ex, "Form 471 window check");
            var text = surface == LookupSurface.Windows
                ? "Could not verify window dates. Check USAC's official announcements; filing lookup remains available."
                : "Could not verify filing-window dates. Check USAC's official announcements; lookups remain available.";
            return new WindowBannerView(text, null, false);
        }
    }

    public List<string[]> ExportRows(
        int year,
        FilingForm form,
        string ben,
        string fallbackCustomer,
        DateTimeOffset fetchedAt,
        IReadOnlyList<Filing> results,
        IReadOnlySet<string> selectedNumbers,
        bool? hasNewCycleForm470)
    {
        var budget = directory.GetBudget(ben, year);
        var rows = new List<string[]>
        {
            new[] { "ExportedUTC", "LookupFetchedUTC", "DirectoryUpdatedUTC", "FundingYear", "BEN", "Customer", "LookupResult", "FormType", "ApplicationNumber", "FilingStatus", "CertifiedDate", "Nickname", "Services", "PDF", "BudgetCycle", "USACBudgetStatus", "BudgetDisplayStatus", "TotalC2BudgetPreDiscount", "CommittedC2BudgetPreDiscount", "PendingC2BudgetPreDiscount", "ReportedRemainingC2BudgetPreDiscount" }
        };
        var exported = clock().ToUniversalTime().ToString("O", CultureInfo.InvariantCulture);
        var budgetState = budget is null ? "Not reported" : BudgetRow.ForecastView(budget.Version, hasNewCycleForm470) ? "Forecast" : budget.Version;
        var items = results.Count > 0
            ? results.Where(r => selectedNumbers.Contains(r.Number)).ToList()
            : new List<Filing> { new("", ben, budget?.Name ?? fallbackCustomer, "", "", "", "") };
        var emptyResult = surface == LookupSurface.Android
            ? "No certified filing found in published data"
            : $"No certified Form {(int)form} found in published data";
        foreach (var filing in items)
        {
            rows.Add(
            [
                exported,
                fetchedAt.ToString("O", CultureInfo.InvariantCulture),
                directory.LastUpdated,
                year.ToString(CultureInfo.InvariantCulture),
                ben,
                filing.Name,
                results.Count > 0 ? "Filed" : emptyResult,
                ((int)form).ToString(CultureInfo.InvariantCulture),
                filing.Number,
                results.Count > 0 ? filing.Status : "",
                filing.Certified,
                filing.Nickname,
                filing.Services,
                filing.Pdf,
                budget?.Cycle ?? "Not reported",
                budget?.Version ?? "Not reported",
                budgetState,
                budget?.Total ?? "Not reported",
                budget?.Committed ?? "Not reported",
                budget?.Pending ?? "Not reported",
                budget?.Available ?? "Not reported"
            ]);
        }

        return rows;
    }

    public static string SuggestedWatchlistName(string selectedName, string? budgetName) =>
        selectedName.Length > 0 ? selectedName : budgetName ?? "";

    public static bool CanChooseMatch(bool busy, bool matchesVisible, int selectedCount) =>
        !busy && matchesVisible && selectedCount == 1;

    public static bool CanSaveMatches(bool busy, bool matchesVisible, int selectedCount) =>
        !busy && matchesVisible && selectedCount > 0;

    public static bool CanSaveBen(bool busy, string? ben) => !busy && (ben?.Length ?? 0) > 0;
}
