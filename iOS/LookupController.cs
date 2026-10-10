using System.Globalization;
using ERate470Lookup.Core;
using Foundation;
using UIKit;

namespace ERate470Lookup.iOS;

public sealed class LookupController : UITableViewController
{
    readonly HttpClient http = new() { Timeout = TimeSpan.FromSeconds(45) };
    readonly UITextField name = Field("California entity name", UIKeyboardType.Default);
    readonly UITextField ben = Field("BEN (digits only)", UIKeyboardType.NumberPad);
    readonly UITextField year = Field("Funding year (2016–2100)", UIKeyboardType.NumberPad);
    readonly UISegmentedControl form = new(new[] { "Form 470", "Form 471" });
    readonly List<UIControl> controls = [];
    readonly List<UITableViewCell> inputCells = [];
    readonly HashSet<string> selected = [];
    readonly string cache = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ERateLookup");
    List<Entity> matches = [];
    List<Filing> filings = [];
    UsacClient? client;
    CaliforniaDirectory? directory;
    FilingWindow? windows;
    CancellationTokenSource? operation;
    UIBarButtonItem share = null!;
    UIBarButtonItem cancel = null!;
    bool ready, busy, complete;
    string status = "Loading bundled California data…", budgetText = "", resultBen = "";
    int resultYear;
    FilingForm resultForm;
    DateTimeOffset fetched;
    bool? hasCycle470;

    public LookupController() : base(UITableViewStyle.InsetGrouped) { }

    public override void ViewDidLoad()
    {
        base.ViewDidLoad();
        Title = "E-rate Lookup";
        TableView.RowHeight = UITableView.AutomaticDimension;
        TableView.EstimatedRowHeight = 70;
        TableView.KeyboardDismissMode = UIScrollViewKeyboardDismissMode.OnDrag;
        year.Text = (DateTime.Now.Year + 1).ToString(CultureInfo.InvariantCulture);
        form.SelectedSegment = 0;
        controls.AddRange(new UIControl[] { name, ben, year, form });
        inputCells.Add(Host(name)); inputCells.Add(Host(ben));
        inputCells.Add(Host(year)); inputCells.Add(Host(form));
        inputCells.Add(Host(Row(Button("Search CA", SearchLocalAsync), Button("Search US", SearchNationalAsync))));
        inputCells.Add(Host(Row(Button("Look up", () => LookupAsync(false)), Button("Refresh filings", () => LookupAsync(true)))));
        inputCells.Add(Host(Row(Button("Refresh CA data", RefreshDirectoryAsync), Button("Select all", SelectAllAsync))));
        name.EditingChanged += (_, _) => { matches.Clear(); InvalidateResults(); };
        ben.EditingChanged += (_, _) => InvalidateResults();
        year.EditingChanged += (_, _) => InvalidateResults();
        form.ValueChanged += (_, _) => InvalidateResults();
        share = new UIBarButtonItem(UIBarButtonSystemItem.Action, (_, _) => ChooseExport());
        cancel = new UIBarButtonItem("Cancel", UIBarButtonItemStyle.Plain, (_, _) => operation?.Cancel());
        NavigationItem.RightBarButtonItem = share;
        NavigationItem.LeftBarButtonItem = cancel;
        _ = InitializeAsync();
    }

    async Task InitializeAsync()
    {
        await RunAsync(async ct =>
        {
            // Seed import does disk work off the UI thread to keep launch responsive.
            await Task.Run(() =>
            {
                client = new UsacClient(http, cache);
                directory = new CaliforniaDirectory(http, cache);
                directory.LoadSeedIfEmpty();
                windows = new FilingWindow(http, cache);
            }, ct);
            ready = true;
            status = "Search the bundled CA directory or enter a BEN. Filings require internet unless a fresh cached query is available.";
            budgetText = "California snapshot: " + directory!.LastUpdated;
        }, initializing: true);
    }

    void InvalidateResults()
    {
        complete = false; filings.Clear(); selected.Clear(); resultBen = "";
        budgetText = directory is null ? "" : "California snapshot: " + directory.LastUpdated;
        status = "Criteria changed. Run a new lookup.";
        share.Enabled = false;
        // Do not reload the input section while the keyboard is editing a field.
        TableView.ReloadSections(NSIndexSet.FromNSRange(new NSRange(1, 3)), UITableViewRowAnimation.None);
    }

    async Task RunAsync(Func<CancellationToken, Task> action, bool initializing = false)
    {
        if (busy || (!ready && !initializing)) return;
        busy = true; operation = new CancellationTokenSource();
        foreach (var control in controls) control.Enabled = false;
        share.Enabled = false; cancel.Enabled = true;
        View!.EndEditing(true);
        try { await action(operation.Token); }
        catch (OperationCanceledException) { status = "Request canceled or timed out. Try again when ready."; }
        catch (Exception ex) { status = "Could not complete the request: " + ex.Message; }
        finally
        {
            operation.Dispose(); operation = null; busy = false;
            foreach (var control in controls) control.Enabled = ready;
            cancel.Enabled = false; share.Enabled = complete;
            TableView.ReloadData();
        }
    }

    Task SearchLocalAsync() => RunAsync(async ct =>
    {
        InvalidateResults();
        var term = name.Text?.Trim() ?? "";
        if (term.Length < 2) throw new ArgumentException("Enter at least two letters of the entity name.");
        matches = await Task.Run(() => directory!.Search(term), ct);
        status = $"{matches.Count} CA matches (up to 15). Tap an entity to set its BEN. Narrow the name or use Search US if needed.";
    });

    Task SearchNationalAsync() => RunAsync(async ct =>
    {
        InvalidateResults();
        var term = name.Text?.Trim() ?? "";
        if (term.Length < 2) throw new ArgumentException("Enter at least two letters of the entity name.");
        matches.Clear();
        var result = await client!.FindEntitiesAsync(term, "", false, ct);
        matches = result.Data.Matches;
        status = $"{matches.Count} nationwide matches from published Form 470 data. " +
            (result.Data.Limited ? "Limit reached; narrow the name. " : "") + $"Fetched {result.FetchedAt.LocalDateTime:g}. Tap a match.";
    });

    Task LookupAsync(bool refresh) => RunAsync(async ct =>
    {
        InvalidateResults();
        var requestedBen = ben.Text?.Trim() ?? "";
        if (!int.TryParse(year.Text, out var requestedYear)) throw new ArgumentException("Enter a funding year between 2016 and 2100.");
        var requestedForm = form.SelectedSegment == 0 ? FilingForm.Form470 : FilingForm.Form471;
        _ = UsacClient.FilingFilter(requestedBen, requestedYear, requestedForm);
        status = "Querying USAC…"; TableView.ReloadData();
        var result = await client!.GetFilingsAsync(requestedBen, requestedYear, requestedForm, refresh, ct);
        ct.ThrowIfCancellationRequested();
        resultBen = requestedBen; resultYear = requestedYear; resultForm = requestedForm;
        filings = result.Data; fetched = result.FetchedAt; hasCycle470 = null;
        var notes = "";
        if (requestedYear >= 2026 && requestedYear <= 2030)
        {
            try { hasCycle470 = (await client.HasNewCycleForm470Async(resultBen, refresh, ct)).Data; }
            catch (HttpRequestException) { notes += " Budget eligibility cross-check unavailable."; }
        }
        if (requestedForm == FilingForm.Form471)
        {
            try { notes += "\n" + (await windows!.GetAsync(requestedYear, ct)).Message(DateTimeOffset.UtcNow); }
            catch (HttpRequestException) { notes += " Filing-window dates unavailable; check USAC announcements."; }
        }
        ct.ThrowIfCancellationRequested();
        foreach (var filing in filings) selected.Add(filing.Number);
        UpdateBudget();
        status = (filings.Count > 0 ? $"{filings.Count} published Form {(int)resultForm} applications." :
            $"No certified Form {(int)resultForm} found in published data; this does not prove no filing exists.") +
            $" BEN {resultBen}, FY{resultYear}. {(result.FromCache ? "Cached" : "Live")}; fetched {fetched.LocalDateTime:g}." + notes;
        complete = true;
    });

    void UpdateBudget()
    {
        var b = directory!.GetBudget(resultBen, resultYear);
        if (b is null) { budgetText = "C2 budget not reported in the CA snapshot. This is not a zero budget. Snapshot: " + directory.LastUpdated; return; }
        var forecast = BudgetRow.ForecastView(b.Version, hasCycle470);
        budgetText = $"{(forecast ? "Forecast C2 budget" : "C2 budget")} · {b.Cycle}\n{b.Name}\nUSAC status: {b.Version}\n" +
            $"Total: {BudgetRow.Money(b.Total)}\nCommitted: {BudgetRow.Money(b.Committed)}\nPending: {BudgetRow.Money(b.Pending)}\n" +
            $"{(forecast ? "Forecast remaining (not confirmed)" : "Reported available")}: {BudgetRow.Money(b.Available)}\n" +
            "Pre-discount figures. Committed is not disbursed. Form 470 does not confirm a budget.\nSnapshot: " + directory.LastUpdated;
        if (hasCycle470 == false) budgetText += "\nNo certified Form 470 found for 2026 or 2027.";
        if (decimal.TryParse(b.Available, NumberStyles.Number, CultureInfo.InvariantCulture, out var remaining) && remaining < 0)
            budgetText += "\nNegative remaining balance: review with USAC.";
    }

    Task RefreshDirectoryAsync() => RunAsync(async ct =>
    {
        await directory!.RefreshAsync(ct);
        if (complete) UpdateBudget(); else budgetText = "California snapshot: " + directory.LastUpdated;
        status = "California directory and C2 data refreshed. Run a lookup to see filing query status.";
    });

    Task SelectAllAsync()
    {
        if (!complete) return Task.CompletedTask;
        if (selected.Count == filings.Count) selected.Clear(); else foreach (var f in filings) selected.Add(f.Number);
        TableView.ReloadData();
        return Task.CompletedTask;
    }

    public override nint NumberOfSections(UITableView tableView) => 4;
    public override nint RowsInSection(UITableView tableView, nint section) => section switch
    { 0 => inputCells.Count, 1 => matches.Count, 2 => 2, 3 => filings.Count, _ => 0 };
    public override string? TitleForHeader(UITableView tableView, nint section) => section switch
    { 0 => "Search and funding year", 1 => matches.Count > 0 ? "Choose an entity" : null, 2 => "Status and California C2 budget", 3 => complete ? $"Filings · {selected.Count} selected" : null, _ => null };
    public override string? TitleForFooter(UITableView tableView, nint section) => section == 3 && complete
        ? "Tap a filing to select it for export. Tap ⓘ for details and its PDF. Use the share button for Excel or CSV." : null;

    public override UITableViewCell GetCell(UITableView tableView, NSIndexPath indexPath)
    {
        if (indexPath.Section == 0) return inputCells[indexPath.Row];
        var cell = tableView.DequeueReusableCell("text") ?? new UITableViewCell(UITableViewCellStyle.Subtitle, "text");
        cell.TextLabel!.Lines = 0; cell.DetailTextLabel!.Lines = 0;
        cell.TextLabel.Font = UIFont.PreferredBody;
        cell.TextLabel.AdjustsFontForContentSizeCategory = true;
        cell.DetailTextLabel.Font = UIFont.PreferredCaption1;
        cell.DetailTextLabel.AdjustsFontForContentSizeCategory = true;
        cell.Accessory = UITableViewCellAccessory.None;
        cell.SelectionStyle = UITableViewCellSelectionStyle.Default;
        cell.DetailTextLabel.Text = "";
        if (indexPath.Section == 1)
        {
            var e = matches[indexPath.Row]; cell.TextLabel.Text = e.Name;
            cell.DetailTextLabel.Text = $"BEN {e.Ben} · {e.City}, {e.State}"; cell.Accessory = UITableViewCellAccessory.DisclosureIndicator;
        }
        else if (indexPath.Section == 2)
        {
            cell.TextLabel.Text = indexPath.Row == 0 ? status : budgetText;
            cell.SelectionStyle = UITableViewCellSelectionStyle.None;
        }
        else
        {
            var f = filings[indexPath.Row];
            cell.TextLabel.Text = $"{(selected.Contains(f.Number) ? "☑" : "☐")} {f.Number} · {f.Nickname}";
            cell.DetailTextLabel.Text = $"{f.Certified} · {f.Status}\n{f.Services}";
            cell.Accessory = UITableViewCellAccessory.DetailButton;
        }
        return cell;
    }

    public override void RowSelected(UITableView tableView, NSIndexPath indexPath)
    {
        tableView.DeselectRow(indexPath, true);
        if (busy) return;
        if (indexPath.Section == 1)
        {
            var entity = matches[indexPath.Row]; ben.Text = entity.Ben; name.Text = entity.Name;
            matches.Clear(); InvalidateResults(); status = $"Selected {entity.Name}. Tap Look up."; tableView.ReloadData();
        }
        else if (indexPath.Section == 3 && complete)
        {
            var number = filings[indexPath.Row].Number;
            if (!selected.Add(number)) selected.Remove(number);
            tableView.ReloadData();
        }
    }

    public override void AccessoryButtonTapped(UITableView tableView, NSIndexPath indexPath)
    {
        if (indexPath.Section != 3 || busy || !complete) return;
        var f = filings[indexPath.Row];
        var dialog = UIAlertController.Create(f.Number, $"{f.Name}\n{f.Nickname}\n{f.Status} · {f.Certified}\n{f.Services}", UIAlertControllerStyle.Alert);
        if (Uri.TryCreate(f.Pdf, UriKind.Absolute, out var url) && url.Scheme == "https")
            dialog.AddAction(UIAlertAction.Create("Open PDF", UIAlertActionStyle.Default, _ =>
                UIApplication.SharedApplication.OpenUrl(new NSUrl(url.AbsoluteUri), new UIApplicationOpenUrlOptions(), success =>
                { if (!success) BeginInvokeOnMainThread(() => Alert("The PDF link could not be opened.")); })));
        dialog.AddAction(UIAlertAction.Create("Close", UIAlertActionStyle.Cancel, null));
        PresentViewController(dialog, true, null);
    }

    void ChooseExport()
    {
        if (!complete || busy) return;
        if (filings.Count > 0 && selected.Count == 0) { Alert("Select at least one filing to export."); return; }
        var dialog = UIAlertController.Create("Export selected filings", "Save to Files, AirDrop, or share with another app.", UIAlertControllerStyle.ActionSheet);
        dialog.AddAction(UIAlertAction.Create("Excel (.xlsx)", UIAlertActionStyle.Default, _ => Export("xlsx")));
        dialog.AddAction(UIAlertAction.Create("CSV (.csv)", UIAlertActionStyle.Default, _ => Export("csv")));
        dialog.AddAction(UIAlertAction.Create("Cancel", UIAlertActionStyle.Cancel, null));
        if (dialog.PopoverPresentationController is { } popover) popover.BarButtonItem = share;
        PresentViewController(dialog, true, null);
    }

    void Export(string extension)
    {
        try
        {
            var b = directory!.GetBudget(resultBen, resultYear);
            var rows = new List<string[]> { new[] { "ExportedUTC", "LookupFetchedUTC", "DirectoryUpdatedUTC", "FundingYear", "BEN", "Customer", "LookupResult", "FormType", "ApplicationNumber", "FilingStatus", "CertifiedDate", "Nickname", "Services", "PDF", "BudgetCycle", "USACBudgetStatus", "BudgetDisplayStatus", "TotalC2BudgetPreDiscount", "CommittedC2BudgetPreDiscount", "PendingC2BudgetPreDiscount", "ReportedRemainingC2BudgetPreDiscount" } };
            var items = filings.Where(f => selected.Contains(f.Number)).ToList();
            if (filings.Count == 0) items.Add(new Filing("", resultBen, b?.Name ?? "", "", "", "", ""));
            var now = DateTimeOffset.UtcNow.ToString("O");
            foreach (var f in items) rows.Add(new[] { now, fetched.ToString("O"), directory.LastUpdated, resultYear.ToString(CultureInfo.InvariantCulture), resultBen, f.Name,
                filings.Count > 0 ? "Filed" : "No certified filing found in published data", ((int)resultForm).ToString(CultureInfo.InvariantCulture), f.Number,
                filings.Count > 0 ? f.Status : "", f.Certified, f.Nickname, f.Services, f.Pdf, b?.Cycle ?? "Not reported", b?.Version ?? "Not reported",
                b is null ? "Not reported" : BudgetRow.ForecastView(b.Version, hasCycle470) ? "Forecast" : b.Version,
                b?.Total ?? "Not reported", b?.Committed ?? "Not reported", b?.Pending ?? "Not reported", b?.Available ?? "Not reported" });
            var folder = Path.Combine(Path.GetTempPath(), "ERate-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(folder);
            var path = Path.Combine(folder, $"Form{(int)resultForm}_{resultBen}_FY{resultYear}.{extension}");
            if (extension == "csv") DataExporter.WriteCsv(path, rows); else DataExporter.WriteExcel(path, rows);
            var activity = new UIActivityViewController(new NSObject[] { NSUrl.FromFilename(path) }, null);
            if (activity.PopoverPresentationController is { } popover) popover.BarButtonItem = share;
            activity.CompletionWithItemsHandler = (_, _, _, _) => { try { Directory.Delete(folder, true); } catch (IOException) { } };
            PresentViewController(activity, true, null);
        }
        catch (Exception ex) { Alert("Export failed: " + ex.Message); }
    }

    void Alert(string text)
    {
        var dialog = UIAlertController.Create("E-rate Lookup", text, UIAlertControllerStyle.Alert);
        dialog.AddAction(UIAlertAction.Create("OK", UIAlertActionStyle.Default, null));
        PresentViewController(dialog, true, null);
    }
    static UITextField Field(string placeholder, UIKeyboardType keyboard) => new()
    { Placeholder = placeholder, AccessibilityLabel = placeholder, KeyboardType = keyboard, BorderStyle = UITextBorderStyle.RoundedRect,
      Font = UIFont.PreferredBody, AdjustsFontForContentSizeCategory = true, AutocorrectionType = UITextAutocorrectionType.No };
    UIButton Button(string title, Func<Task> action)
    {
        var button = new UIButton(UIButtonType.System); button.SetTitle(title, UIControlState.Normal);
        button.TitleLabel!.Font = UIFont.PreferredBody; button.TitleLabel.AdjustsFontForContentSizeCategory = true;
        button.TouchUpInside += async (_, _) => await action(); controls.Add(button); return button;
    }
    static UIStackView Row(params UIView[] views) => new(views)
    { Axis = UILayoutConstraintAxis.Horizontal, Distribution = UIStackViewDistribution.FillEqually, Spacing = 8 };
    static UITableViewCell Host(UIView view)
    {
        var cell = new UITableViewCell { SelectionStyle = UITableViewCellSelectionStyle.None };
        view.TranslatesAutoresizingMaskIntoConstraints = false; cell.ContentView.AddSubview(view);
        NSLayoutConstraint.ActivateConstraints(new[] { view.LeadingAnchor.ConstraintEqualTo(cell.ContentView.LeadingAnchor, 16),
            view.TrailingAnchor.ConstraintEqualTo(cell.ContentView.TrailingAnchor, -16), view.TopAnchor.ConstraintEqualTo(cell.ContentView.TopAnchor, 8),
            view.BottomAnchor.ConstraintEqualTo(cell.ContentView.BottomAnchor, -8), view.HeightAnchor.ConstraintGreaterThanOrEqualTo(44) });
        return cell;
    }
}
