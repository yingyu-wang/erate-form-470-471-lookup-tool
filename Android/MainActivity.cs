using System.Globalization;
using Android.App;
using Android.Content;
using Android.Content.PM;
using Android.Graphics;
using Android.OS;
using Android.Text;
using Android.Views;
using Android.Widget;
using ERateLookup.Core;
using Uri = System.Uri;
using Path = System.IO.Path;
using Entity = ERateLookup.Core.Entity;
using OperationCanceledException = System.OperationCanceledException;

namespace ERateLookup.AndroidApp;

[Activity(Name = "com.yingyuwang.eratelookup.android.MainActivity", Label = "E-rate Lookup", MainLauncher = true,
    Exported = true, ConfigurationChanges = ConfigChanges.Orientation | ConfigChanges.ScreenSize | ConfigChanges.KeyboardHidden,
    WindowSoftInputMode = SoftInput.AdjustResize)]
public class MainActivity : Activity
{
    const int SaveDocumentRequest = 470;
    readonly HttpClient http = new() { Timeout = TimeSpan.FromSeconds(45) };
    readonly List<View> controls = [];
    readonly HashSet<string> selected = [];
    EditText name = null!, ben = null!, year = null!;
    Spinner form = null!;
    TextView status = null!, budget = null!;
    LinearLayout suggestions = null!, results = null!;
    Button export = null!, cancel = null!, selectAll = null!, saveMatches = null!;
    readonly Dictionary<string, Entity> selectedMatches = [];
    UsacClient? client;
    CaliforniaDirectory? directory;
    FilingWindow? windows;
    LookupViewModel? screen;
    CancellationTokenSource? operation;
    List<Filing> filings = [];
    bool ready, busy, complete, destroyed, suppressChanges;
    string cache = "", resultBen = "", matchBen = "", matchName = "";
    string? pendingExport;
    int resultYear, resultPage;
    FilingForm resultForm;
    DateTimeOffset fetched;
    bool? hasCycle470;

    protected override void OnCreate(Bundle? savedInstanceState)
    {
        base.OnCreate(savedInstanceState);
        cache = Path.Combine(FilesDir!.AbsolutePath, "ERateLookup");
        var scroll = new ScrollView(this) { FillViewport = true };
        var page = Column();
        page.SetPadding(Dp(16), Dp(16), Dp(16), Dp(16));
        scroll.AddView(page);
        SetContentView(scroll);
        // Android 15/16 draw edge to edge; protect controls from system bars and keyboard.
        scroll.SetOnApplyWindowInsetsListener(new InsetsListener());
        scroll.RequestApplyInsets();
        var heading = Label("E-rate Lookup", 26);
        heading.SetTypeface(null, TypefaceStyle.Bold);
        page.AddView(heading);
        page.AddView(Label("Form 470 / 471 · California directory and nationwide filings"));
        name = Field(page, "Entity name", InputTypes.ClassText, "At least two letters");
        ben = Field(page, "BEN", InputTypes.ClassNumber, "Digits only");
        year = Field(page, "Funding year", InputTypes.ClassNumber, "2016–2100");
        year.Text = (DateTime.Now.Year + 1).ToString(CultureInfo.InvariantCulture);
        page.AddView(Label("Form type"));
        form = new Spinner(this) { ContentDescription = "Form type" };
        var adapter = new ArrayAdapter<string>(this, global::Android.Resource.Layout.SimpleSpinnerItem, new[] { "Form 470", "Form 471" });
        adapter.SetDropDownViewResource(global::Android.Resource.Layout.SimpleSpinnerDropDownItem);
        form.Adapter = adapter;
        page.AddView(form); controls.Add(form);
        page.AddView(Row(ActionButton("Search CA", SearchLocalAsync), ActionButton("Search US", SearchNationalAsync)));
        suggestions = Column(); page.AddView(suggestions);
        saveMatches = ActionButton("Add selected BENs to watchlist", AddMatchesToWatchlistAsync);
        page.AddView(saveMatches);
        page.AddView(Row(ActionButton("Look up", () => LookupAsync(false)), ActionButton("Refresh filings", () => LookupAsync(true))));
        page.AddView(ActionButton("Refresh CA data", RefreshDirectoryAsync));
        page.AddView(ActionButton("Saved BENs / Watchlists", () => { StartActivity(new Intent(this, typeof(PortfolioActivity))); return Task.CompletedTask; }));
        page.AddView(ActionButton("Add BEN to watchlist", AddBenToWatchlistAsync));
        status = Label("Loading bundled California data…"); status.SetTextIsSelectable(true); page.AddView(status);
        budget = Label(""); budget.SetTextIsSelectable(true); page.AddView(budget);
        cancel = ActionButton("Cancel request", () => { operation?.Cancel(); return Task.CompletedTask; }, false);
        selectAll = ActionButton("Select / clear all", () => { if (complete) { if (selected.Count == filings.Count) selected.Clear(); else foreach (var f in filings) selected.Add(f.Number); RenderResults(); } return Task.CompletedTask; }, false);
        export = ActionButton("Save selected (CSV / Excel / JSON)", () => { ChooseExport(); return Task.CompletedTask; }, false);
        page.AddView(Row(selectAll, cancel)); page.AddView(export);
        results = Column(); page.AddView(results);
        page.AddView(Label("Independent tool; not an official USAC product. Budgets are snapshots, not confirmed eligibility. Internet is required for live filings."));
        suppressChanges = true;
        if (savedInstanceState is not null)
        {
            name.Text = savedInstanceState.GetString("name") ?? "";
            ben.Text = savedInstanceState.GetString("ben") ?? "";
            year.Text = savedInstanceState.GetString("year") ?? year.Text;
            form.SetSelection(savedInstanceState.GetInt("form", 0));
            var pending = savedInstanceState.GetString("pendingExport");
            if (pending is not null && Path.GetDirectoryName(pending) == CacheDir!.AbsolutePath && File.Exists(pending)) pendingExport = pending;
        }
        suppressChanges = false;
        name.TextChanged += (_, _) => CriteriaChanged(); ben.TextChanged += (_, _) => CriteriaChanged();
        year.TextChanged += (_, _) => CriteriaChanged(); form.ItemSelected += (_, _) => CriteriaChanged();
        _ = InitializeAsync();
    }

    async Task InitializeAsync() => await RunAsync(async ct =>
    {
        await Task.Run(() => { client = new UsacClient(http, cache); directory = new CaliforniaDirectory(http, cache); directory.LoadSeedIfEmpty(); windows = new FilingWindow(http, cache); screen = new LookupViewModel(client, directory, windows, LookupSurface.Android); }, ct);
        ct.ThrowIfCancellationRequested(); if (destroyed) return;
        ready = true;
        var refreshed = true;
        if (directory!.NeedsRefresh)
        {
            status.Text = "Checking for updated California data…";
            refreshed = await AndroidBehavior.RefreshIfStaleAsync(directory, ct);
        }
        ct.ThrowIfCancellationRequested(); if (destroyed) return;
        status.Text = "Ready. Search the bundled CA directory or enter a BEN. Filings use USAC or a fresh two-hour cache.";
        if (!refreshed) status.Text += " Online CA refresh unavailable; the saved snapshot remains available. Use Refresh CA data to retry.";
        budget.Text = "California snapshot: " + directory!.LastUpdated;
    }, initializing: true);

    void CriteriaChanged()
    {
        if (suppressChanges || busy) return;
        InvalidateResults(); ClearMatches();
        if (ready) status.Text = "Criteria changed. Run a new lookup.";
    }

    void InvalidateResults()
    {
        complete = false; filings.Clear(); selected.Clear(); resultBen = ""; hasCycle470 = null; resultPage = 0;
        results.RemoveAllViews();
        budget.Text = directory is null ? "" : "California snapshot: " + directory.LastUpdated;
        UpdateControls();
    }

    async Task RunAsync(Func<CancellationToken, Task> action, bool initializing = false)
    {
        if (busy || (pendingExport is not null && !initializing) || (!ready && !initializing)) return;
        busy = true; using var current = new CancellationTokenSource(); operation = current; UpdateControls();
        try { await action(current.Token); }
        catch (OperationCanceledException) { if (!destroyed) status.Text = "Request canceled or timed out. Try again."; }
        catch (Exception ex) when (AppExceptions.IsOperational(ex)) { AppLog.Error(ex, "Android request"); if (!destroyed) status.Text = "Could not complete the request: " + ex.Message; }
        finally { if (ReferenceEquals(operation, current)) operation = null; busy = false; if (!destroyed) UpdateControls(); }
    }

    void UpdateControls()
    {
        foreach (var view in controls) view.Enabled = ready && !busy && pendingExport is null;
        foreach (var group in new[] { suggestions, results })
            for (var i = 0; i < group.ChildCount; i++) group.GetChildAt(i)!.Enabled = ready && !busy && pendingExport is null;
        cancel.Enabled = ready && busy;
        selectAll.Enabled = complete && filings.Count > 0 && !busy && pendingExport is null;
        export.Enabled = complete && !busy && pendingExport is null;
        saveMatches.Enabled = ready && !busy && pendingExport is null && selectedMatches.Count > 0;
    }

    Task SearchLocalAsync() => RunAsync(async ct =>
    {
        InvalidateResults(); ClearMatches();
        var term = name.Text?.Trim() ?? "";
        if (term.Length < 2) throw new ArgumentException("Enter at least two letters of the entity name.");
        var matches = await Task.Run(() => directory!.Search(term), ct);
        ct.ThrowIfCancellationRequested(); if (destroyed) return;
        RenderMatches(matches);
        status.Text = $"{matches.Count} CA matches (up to 15). Check districts to save without a filing lookup, or tap Use BEN for one lookup.";
    });

    Task SearchNationalAsync() => RunAsync(async ct =>
    {
        InvalidateResults(); ClearMatches();
        var term = name.Text?.Trim() ?? "";
        if (term.Length < 2) throw new ArgumentException("Enter at least two letters of the entity name.");
        var response = await client!.FindEntitiesAsync(term, "", false, ct);
        ct.ThrowIfCancellationRequested(); if (destroyed) return;
        RenderMatches(response.Data.Matches);
        status.Text = $"{response.Data.Matches.Count} US matches from published Form 470 data. Fetched {response.FetchedAt.LocalDateTime:g}. " + (response.Data.Limited ? "Limit reached; narrow the name." : "Check districts to save, or tap Use BEN for one lookup.");
    });

    void RenderMatches(List<Entity> matches)
    {
        ClearMatches();
        foreach (var entity in matches)
        {
            var check = new CheckBox(this) { Text = entity.ToString(), ContentDescription = "Select " + entity.ToString() };
            check.CheckedChange += (_, e) => { if (e.IsChecked) selectedMatches[entity.Ben] = entity; else selectedMatches.Remove(entity.Ben); UpdateControls(); };
            var button = new Button(this) { Text = "Use BEN " + entity.Ben, ContentDescription = "Look up " + entity.ToString() };
            button.Click += (_, _) => { if (busy || pendingExport is not null) return; ben.Text = entity.Ben; matchBen = entity.Ben; matchName = entity.Name; ClearMatches(); status.Text = "BEN selected. Choose year and form, then Look up."; };
            suggestions.AddView(check); suggestions.AddView(button);
        }
        UpdateControls();
    }
    void ClearMatches() { selectedMatches.Clear(); suggestions.RemoveAllViews(); if (saveMatches is not null) saveMatches.Enabled = false; }

    Task AddMatchesToWatchlistAsync()
    {
        if (destroyed || !ready || busy || pendingExport is not null) return Task.CompletedTask;
        var entries = selectedMatches.Values.Select(e => new WatchlistEntry(e.Ben, e.Name, "")).ToList();
        return entries.Count == 0 ? Task.CompletedTask : SaveToWatchlistAsync(entries, true);
    }

    Task AddBenToWatchlistAsync()
    {
        if (destroyed) return Task.CompletedTask;
        if (!ready || busy || pendingExport is not null) return Task.CompletedTask;
        var benText = ben.Text?.Trim() ?? "";
        if (benText.Length == 0) { Alert("Identify a BEN first — search by name or enter a BEN."); return Task.CompletedTask; }
        var suggested = benText == matchBen && matchName.Length > 0 ? matchName : "";
        if (suggested.Length == 0 && directory is not null && int.TryParse(year.Text, out var y)) suggested = directory.GetBudget(benText, y)?.Name ?? "";
        return SaveToWatchlistAsync([new(benText, suggested, "")], false);
    }
    Task SaveToWatchlistAsync(IReadOnlyList<WatchlistEntry> entries, bool skipExisting)
    {
        // The action button reports store errors without disabling ordinary lookups.
        var store = new PortfolioStore(cache);
        var watchlists = store.Lists();
        if (watchlists.Count == 0) { Alert("Create a watchlist in Saved BENs / Watchlists first."); return Task.CompletedTask; }
        var layout = new LinearLayout(this) { Orientation = Orientation.Vertical };
        layout.SetPadding(Dp(24), Dp(16), Dp(24), Dp(8));
        var spinner = new Spinner(this) { ContentDescription = "Watchlist" };
        spinner.Adapter = new ArrayAdapter<string>(this, global::Android.Resource.Layout.SimpleSpinnerItem, watchlists.Select(l => l.Name).ToArray());
        layout.AddView(spinner);
        var nameBox = new EditText(this) { Hint = "Display name (optional)", Text = entries[0].Name };
        nameBox.SetSingleLine(true);
        var tagsBox = new EditText(this) { Hint = "Tags (optional)" };
        tagsBox.SetSingleLine(true);
        if (entries.Count == 1) layout.AddView(nameBox);
        else
        {
            var preview = Column();
            foreach (var entry in entries) preview.AddView(Label(entry.ToString()));
            var scroll = new ScrollView(this); scroll.AddView(preview);
            layout.AddView(Label("Selected districts · existing BENs will be kept"));
            layout.AddView(scroll, new LinearLayout.LayoutParams(ViewGroup.LayoutParams.MatchParent, Dp(180)));
        }
        layout.AddView(tagsBox);
        new AlertDialog.Builder(this)!.SetTitle(entries.Count == 1 ? $"Add BEN {entries[0].Ben} to watchlist" : $"Add {entries.Count} BENs to watchlist")!.SetView(layout)
            !.SetPositiveButton("Save", (_, _) =>
            {
                try
                {
                    if (spinner.SelectedItemPosition < 0 || spinner.SelectedItemPosition >= watchlists.Count) throw new InvalidOperationException("No watchlist available.");
                    var list = watchlists[spinner.SelectedItemPosition];
                    var pending = entries.Select(e => e with { Name = entries.Count == 1 ? nameBox.Text ?? "" : e.Name, Tags = tagsBox.Text ?? "" }).ToList();
                    var added = 1;
                    if (skipExisting) added = store.ImportEntries(list.Id, pending);
                    else store.SaveEntry(list.Id, pending[0]);
                    status.Text = $"{added} BEN(s) added to \"{list.Name}\"; {entries.Count - added} already saved. No filing lookup was needed.";
                }
                    catch (Exception ex) when (AppExceptions.IsOperational(ex)) { AppLog.Error(ex, "Save watchlist entry"); if (!destroyed) Alert("Could not save: " + ex.Message); }
            })
            !.SetNegativeButton("Cancel", (_, _) => { })!.Show();
        return Task.CompletedTask;
    }

    Task LookupAsync(bool refresh) => RunAsync(async ct =>
    {
        InvalidateResults();
        var requestedBen = ben.Text?.Trim() ?? "";
        if (!int.TryParse(year.Text, out var requestedYear)) throw new ArgumentException("Enter a funding year between 2016 and 2100.");
        var requestedForm = form.SelectedItemPosition == 0 ? FilingForm.Form470 : FilingForm.Form471;
        _ = UsacClient.FilingFilter(requestedBen, requestedYear, requestedForm);
        status.Text = "Querying USAC…";
        var outcome = await screen!.LookupAsync(requestedBen, requestedYear, requestedForm, refresh, "", ct);
        ct.ThrowIfCancellationRequested(); if (destroyed) return;
        resultBen = outcome.Ben; resultYear = outcome.Year; resultForm = outcome.Form;
        filings = outcome.Filings.ToList(); fetched = outcome.FetchedAt; hasCycle470 = outcome.HasNewCycleForm470;
        foreach (var f in filings) selected.Add(f.Number);
        complete = true; UpdateBudget(); RenderResults();
        status.Text = outcome.Status;
    });

    void UpdateBudget()
    {
        budget.Text = screen!.BudgetText(resultBen, resultYear, hasCycle470);
    }

    Task RefreshDirectoryAsync() => RunAsync(async ct =>
    {
        await directory!.RefreshAsync(ct); ct.ThrowIfCancellationRequested(); if (destroyed) return;
        if (complete) UpdateBudget(); else budget.Text = "California snapshot: " + directory.LastUpdated;
        status.Text = "California directory and C2 data refreshed. Filing queries have a separate cache.";
    });

    void RenderResults()
    {
        results.RemoveAllViews();
        var pages = AndroidBehavior.PageCount(filings.Count);
        resultPage = Math.Clamp(resultPage, 0, pages - 1);
        results.AddView(Label($"Page {resultPage + 1} of {pages} · up to {AndroidBehavior.PageSize} applications per page. Selection and export include all pages."));
        if (resultPage > 0) AddPageButton("Previous applications", -1);
        foreach (var filing in AndroidBehavior.Page(filings, resultPage))
        {
            var check = new CheckBox(this) { Text = $"Application {filing.Number}\n{filing.Name}\n{filing.Status} · {filing.Certified}\n{filing.Nickname}\n{filing.Services}", Checked = selected.Contains(filing.Number) };
            check.CheckedChange += (_, e) => { if (e.IsChecked) selected.Add(filing.Number); else selected.Remove(filing.Number); };
            results.AddView(check);
            var pdf = new Button(this) { Text = "Open application PDF" };
            pdf.Click += (_, _) => OpenPdf(filing.Pdf); results.AddView(pdf);
        }
        if (resultPage + 1 < pages) AddPageButton("Next applications", 1);
        UpdateControls();
    }

    void AddPageButton(string title, int offset)
    {
        var button = new Button(this) { Text = title };
        button.Click += (_, _) => { if (!ready || busy || pendingExport is not null) return; resultPage += offset; RenderResults(); };
        results.AddView(button);
    }

    void OpenPdf(string address)
    {
        if (!Uri.TryCreate(address, UriKind.Absolute, out var url) || url.Scheme != Uri.UriSchemeHttps) { Alert("No valid HTTPS PDF link is available."); return; }
        try { StartActivity(new Intent(Intent.ActionView, global::Android.Net.Uri.Parse(url.AbsoluteUri))); }
        catch (ActivityNotFoundException) { Alert("No browser or PDF app is installed to open this link."); }
    }

    void ChooseExport()
    {
        if (!complete || busy || pendingExport is not null) return;
        if (filings.Count > 0 && selected.Count == 0) { Alert("Select at least one filing to export."); return; }
        new AlertDialog.Builder(this)!.SetTitle("Save selected filings")!.SetItems(new[] { "Excel (.xlsx)", "CSV (.csv)", "JSON (.json)" }, async (_, e) => await PrepareExportAsync(e.Which switch { 0 => "xlsx", 1 => "csv", _ => "json" }))!.SetNegativeButton("Cancel", (_, _) => { })!.Show();
    }

    async Task PrepareExportAsync(string extension)
    {
        await RunAsync(async ct =>
        {
            ct.ThrowIfCancellationRequested();
            var prepared = screen!.CreateExport(resultYear, resultForm, resultBen, "", fetched, filings, selected, hasCycle470);
            var path = Path.Combine(CacheDir!.AbsolutePath, "export-" + Guid.NewGuid().ToString("N") + "." + extension);
            try
            {
                await Task.Run(() => prepared.Write(path, ct), ct);
                ct.ThrowIfCancellationRequested();
                if (destroyed) return;
                pendingExport = path;
                var intent = new Intent(Intent.ActionCreateDocument);
                intent.AddCategory(Intent.CategoryOpenable);
                intent.SetType(AndroidBehavior.ExportContentType(extension));
                intent.PutExtra(Intent.ExtraTitle, $"Form{(int)resultForm}_{resultBen}_FY{resultYear}.{extension}");
                StartActivityForResult(intent, SaveDocumentRequest);
            }
            catch { pendingExport = null; throw; }
            finally { if (pendingExport != path) { try { File.Delete(path); } catch (IOException) { } } }
        });
    }

    protected override async void OnActivityResult(int requestCode, Result resultCode, Intent? data)
    {
        base.OnActivityResult(requestCode, resultCode, data);
        if (requestCode != SaveDocumentRequest || pendingExport is null) return;
        var path = pendingExport; busy = true;
        using var current = new CancellationTokenSource(); operation = current; UpdateControls();
        try
        {
            if (resultCode == Result.Ok && data?.Data is { } destination)
            {
                using var source = File.OpenRead(path);
                using var target = ContentResolver!.OpenOutputStream(destination, "wt") ?? throw new IOException("The selected location could not be opened.");
                await AndroidBehavior.CopyExportAsync(source, target, current.Token);
                if (!destroyed) status.Text = "Export saved to the selected location.";
            }
            else if (!destroyed) status.Text = "Export canceled.";
        }
        catch (OperationCanceledException) { if (!destroyed) status.Text = "Export canceled. The selected destination may contain an incomplete file; delete it before retrying."; }
        catch (Exception ex) when (AppExceptions.IsOperational(ex)) { AppLog.Error(ex, "Export filings"); if (!destroyed) Alert("Export failed: " + ex.Message); }
        finally { try { File.Delete(path); } catch (IOException) { } pendingExport = null; if (ReferenceEquals(operation, current)) operation = null; busy = false; if (!destroyed) UpdateControls(); }
    }

    protected override void OnSaveInstanceState(Bundle outState)
    {
        outState.PutString("name", name.Text); outState.PutString("ben", ben.Text); outState.PutString("year", year.Text);
        outState.PutInt("form", form.SelectedItemPosition); outState.PutString("pendingExport", pendingExport);
        base.OnSaveInstanceState(outState);
    }
    protected override void OnDestroy() { destroyed = true; operation?.Cancel(); client?.Dispose(); http.Dispose(); base.OnDestroy(); }
    void Alert(string message) => new AlertDialog.Builder(this)!.SetTitle("E-rate Lookup")!.SetMessage(message)!.SetPositiveButton("OK", (_, _) => { })!.Show();
    int Dp(int value) => (int)(value * Resources!.DisplayMetrics!.Density + 0.5f);
    LinearLayout Column() => new(this) { Orientation = Orientation.Vertical };
    TextView Label(string text, float size = 16) { var view = new TextView(this) { Text = text, TextSize = size }; view.SetPadding(0, Dp(8), 0, Dp(8)); return view; }
    EditText Field(LinearLayout parent, string title, InputTypes type, string hint)
    {
        parent.AddView(Label(title)); var view = new EditText(this) { InputType = type, Hint = hint, ContentDescription = title, TextSize = 18 };
        view.SetSingleLine(true); parent.AddView(view); controls.Add(view); return view;
    }
    Button ActionButton(string title, Func<Task> action, bool gate = true)
    {
        var button = new Button(this) { Text = title, ContentDescription = title }; button.SetAllCaps(false);
        button.Click += async (_, _) => { try { await action(); } catch (Exception ex) when (AppExceptions.IsOperational(ex)) { AppLog.Error(ex, title); if (!destroyed) Alert(ex.Message); } };
        if (gate) controls.Add(button); return button;
    }
    LinearLayout Row(params View[] views) { var row = new LinearLayout(this) { Orientation = Orientation.Horizontal }; foreach (var view in views) row.AddView(view, new LinearLayout.LayoutParams(0, ViewGroup.LayoutParams.WrapContent, 1)); return row; }
    sealed class InsetsListener : Java.Lang.Object, View.IOnApplyWindowInsetsListener
    {
        public WindowInsets OnApplyWindowInsets(View? view, WindowInsets? insets)
        {
            if (view is not null && insets is not null && global::Android.OS.Build.VERSION.SdkInt >= BuildVersionCodes.R)
            {
                var bars = insets.GetInsets(WindowInsets.Type.SystemBars() | WindowInsets.Type.Ime());
                view.SetPadding(bars.Left, bars.Top, bars.Right, bars.Bottom);
            }
            else if (view is not null && insets is not null) view.SetPadding(insets.SystemWindowInsetLeft, insets.SystemWindowInsetTop, insets.SystemWindowInsetRight, insets.SystemWindowInsetBottom);
            return insets!;
        }
    }
}
