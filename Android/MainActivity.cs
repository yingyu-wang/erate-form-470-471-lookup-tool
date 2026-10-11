using System.Globalization;
using Android.App;
using Android.Content;
using Android.Content.PM;
using Android.Graphics;
using Android.OS;
using Android.Text;
using Android.Views;
using Android.Widget;
using ERate470Lookup.Core;
using Uri = System.Uri;
using Path = System.IO.Path;

namespace ERate470Lookup.AndroidApp;

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
    Button export = null!, cancel = null!, selectAll = null!;
    UsacClient? client;
    CaliforniaDirectory? directory;
    FilingWindow? windows;
    CancellationTokenSource? operation;
    List<Filing> filings = [];
    bool ready, busy, complete, destroyed, suppressChanges;
    string cache = "", resultBen = "";
    string? pendingExport;
    int resultYear;
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
        page.AddView(Row(ActionButton("Look up", () => LookupAsync(false)), ActionButton("Refresh filings", () => LookupAsync(true))));
        page.AddView(ActionButton("Refresh CA data", RefreshDirectoryAsync));
        status = Label("Loading bundled California data…"); status.SetTextIsSelectable(true); page.AddView(status);
        budget = Label(""); budget.SetTextIsSelectable(true); page.AddView(budget);
        cancel = ActionButton("Cancel request", () => { operation?.Cancel(); return Task.CompletedTask; }, false);
        selectAll = ActionButton("Select / clear all", () => { if (complete) { if (selected.Count == filings.Count) selected.Clear(); else foreach (var f in filings) selected.Add(f.Number); RenderResults(); } return Task.CompletedTask; }, false);
        export = ActionButton("Save selected CSV / Excel", () => { ChooseExport(); return Task.CompletedTask; }, false);
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
        await Task.Run(() => { client = new UsacClient(http, cache); directory = new CaliforniaDirectory(http, cache); directory.LoadSeedIfEmpty(); windows = new FilingWindow(http, cache); }, ct);
        ct.ThrowIfCancellationRequested(); if (destroyed) return;
        ready = true;
        status.Text = "Ready. Search the bundled CA directory or enter a BEN. Filings use USAC or a fresh two-hour cache.";
        budget.Text = "California snapshot: " + directory!.LastUpdated;
    }, initializing: true);

    void CriteriaChanged()
    {
        if (suppressChanges || busy) return;
        InvalidateResults(); suggestions.RemoveAllViews();
        if (ready) status.Text = "Criteria changed. Run a new lookup.";
    }

    void InvalidateResults()
    {
        complete = false; filings.Clear(); selected.Clear(); resultBen = ""; hasCycle470 = null;
        results.RemoveAllViews();
        budget.Text = directory is null ? "" : "California snapshot: " + directory.LastUpdated;
        UpdateControls();
    }

    async Task RunAsync(Func<CancellationToken, Task> action, bool initializing = false)
    {
        if (busy || (pendingExport is not null && !initializing) || (!ready && !initializing)) return;
        busy = true; operation = new CancellationTokenSource(); UpdateControls();
        try { await action(operation.Token); }
        catch (OperationCanceledException) { if (!destroyed) status.Text = "Request canceled or timed out. Try again."; }
        catch (Exception ex) { if (!destroyed) status.Text = "Could not complete the request: " + ex.Message; }
        finally { operation.Dispose(); operation = null; busy = false; if (!destroyed) UpdateControls(); }
    }

    void UpdateControls()
    {
        foreach (var view in controls) view.Enabled = ready && !busy && pendingExport is null;
        foreach (var group in new[] { suggestions, results })
            for (var i = 0; i < group.ChildCount; i++) group.GetChildAt(i)!.Enabled = ready && !busy && pendingExport is null;
        cancel.Enabled = ready && busy;
        selectAll.Enabled = complete && filings.Count > 0 && !busy && pendingExport is null;
        export.Enabled = complete && !busy && pendingExport is null;
    }

    Task SearchLocalAsync() => RunAsync(async ct =>
    {
        InvalidateResults(); suggestions.RemoveAllViews();
        var term = name.Text?.Trim() ?? "";
        if (term.Length < 2) throw new ArgumentException("Enter at least two letters of the entity name.");
        var matches = await Task.Run(() => directory!.Search(term), ct);
        ct.ThrowIfCancellationRequested(); if (destroyed) return;
        RenderMatches(matches);
        status.Text = $"{matches.Count} CA matches (up to 15). Tap a match to use its BEN; narrow the name or use Search US.";
    });

    Task SearchNationalAsync() => RunAsync(async ct =>
    {
        InvalidateResults(); suggestions.RemoveAllViews();
        var term = name.Text?.Trim() ?? "";
        if (term.Length < 2) throw new ArgumentException("Enter at least two letters of the entity name.");
        var response = await client!.FindEntitiesAsync(term, "", false, ct);
        ct.ThrowIfCancellationRequested(); if (destroyed) return;
        RenderMatches(response.Data.Matches);
        status.Text = $"{response.Data.Matches.Count} US matches from published Form 470 data. Fetched {response.FetchedAt.LocalDateTime:g}. " + (response.Data.Limited ? "Limit reached; narrow the name." : "Tap a match to use its BEN.");
    });

    void RenderMatches(List<Entity> matches)
    {
        foreach (var entity in matches)
        {
            var button = new Button(this) { Text = entity.ToString(), ContentDescription = entity.ToString() };
            button.Click += (_, _) => { if (busy || pendingExport is not null) return; ben.Text = entity.Ben; suggestions.RemoveAllViews(); status.Text = "BEN selected. Choose year and form, then Look up."; };
            suggestions.AddView(button);
        }
    }

    Task LookupAsync(bool refresh) => RunAsync(async ct =>
    {
        InvalidateResults();
        var requestedBen = ben.Text?.Trim() ?? "";
        if (!int.TryParse(year.Text, out var requestedYear)) throw new ArgumentException("Enter a funding year between 2016 and 2100.");
        var requestedForm = form.SelectedItemPosition == 0 ? FilingForm.Form470 : FilingForm.Form471;
        _ = UsacClient.FilingFilter(requestedBen, requestedYear, requestedForm);
        status.Text = "Querying USAC…";
        var response = await client!.GetFilingsAsync(requestedBen, requestedYear, requestedForm, refresh, ct);
        var notes = "";
        bool? cycle470 = null;
        if (requestedYear >= 2026 && requestedYear <= 2030)
        {
            try { cycle470 = (await client.HasNewCycleForm470Async(requestedBen, refresh, ct)).Data; }
            catch (HttpRequestException) { notes += " Budget eligibility cross-check unavailable."; }
        }
        if (requestedForm == FilingForm.Form471) notes += "\n" + (await windows!.GetAsync(requestedYear, ct)).Message(DateTimeOffset.UtcNow);
        ct.ThrowIfCancellationRequested(); if (destroyed) return;
        resultBen = requestedBen; resultYear = requestedYear; resultForm = requestedForm;
        filings = response.Data; fetched = response.FetchedAt; hasCycle470 = cycle470;
        foreach (var f in filings) selected.Add(f.Number);
        complete = true; UpdateBudget(); RenderResults();
        status.Text = (filings.Count == 0 ? "No certified filings found in published data; this does not prove no filing exists." : $"{filings.Count} published Form {(int)resultForm} applications.") + $" BEN {resultBen}, FY{resultYear}. {(response.FromCache ? "Cached" : "Live")}; fetched {fetched.LocalDateTime:g}." + notes;
    });

    void UpdateBudget()
    {
        var b = directory!.GetBudget(resultBen, resultYear);
        if (b is null) { budget.Text = "C2 budget not reported in the CA snapshot. This is not a zero budget. Snapshot: " + directory.LastUpdated; return; }
        var forecast = BudgetRow.ForecastView(b.Version, hasCycle470);
        budget.Text = $"{(forecast ? "Forecast C2 budget" : "C2 budget")} · {b.Cycle}\n{b.Name}\nUSAC status: {b.Version}\nTotal: {BudgetRow.Money(b.Total)}\nCommitted: {BudgetRow.Money(b.Committed)}\nPending: {BudgetRow.Money(b.Pending)}\n{(forecast ? "Forecast remaining (not confirmed)" : "Reported available")}: {BudgetRow.Money(b.Available)}\nPre-discount figures. Committed is not disbursed. Form 470 does not confirm a budget.\nSnapshot: {directory.LastUpdated}";
        if (hasCycle470 == false) budget.Text += "\nNo certified Form 470 found for 2026 or 2027.";
        if (decimal.TryParse(b.Available, NumberStyles.Number, CultureInfo.InvariantCulture, out var remaining) && remaining < 0) budget.Text += "\nNegative remaining balance: review with USAC.";
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
        foreach (var filing in filings)
        {
            var check = new CheckBox(this) { Text = $"Application {filing.Number}\n{filing.Name}\n{filing.Status} · {filing.Certified}\n{filing.Nickname}\n{filing.Services}", Checked = selected.Contains(filing.Number) };
            check.CheckedChange += (_, e) => { if (e.IsChecked) selected.Add(filing.Number); else selected.Remove(filing.Number); };
            results.AddView(check);
            var pdf = new Button(this) { Text = "Open application PDF" };
            pdf.Click += (_, _) => OpenPdf(filing.Pdf); results.AddView(pdf);
        }
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
        new AlertDialog.Builder(this)!.SetTitle("Save selected filings")!.SetItems(new[] { "Excel (.xlsx)", "CSV (.csv)" }, async (_, e) => await PrepareExportAsync(e.Which == 0 ? "xlsx" : "csv"))!.SetNegativeButton("Cancel", (_, _) => { })!.Show();
    }

    async Task PrepareExportAsync(string extension)
    {
        await RunAsync(async ct =>
        {
            var b = directory!.GetBudget(resultBen, resultYear);
            var rows = new List<string[]> { new[] { "ExportedUTC", "LookupFetchedUTC", "DirectoryUpdatedUTC", "FundingYear", "BEN", "Customer", "LookupResult", "FormType", "ApplicationNumber", "FilingStatus", "CertifiedDate", "Nickname", "Services", "PDF", "BudgetCycle", "USACBudgetStatus", "BudgetDisplayStatus", "TotalC2BudgetPreDiscount", "CommittedC2BudgetPreDiscount", "PendingC2BudgetPreDiscount", "ReportedRemainingC2BudgetPreDiscount" } };
            var items = filings.Where(f => selected.Contains(f.Number)).ToList();
            if (filings.Count == 0) items.Add(new Filing("", resultBen, b?.Name ?? "", "", "", "", ""));
            foreach (var f in items) rows.Add(new[] { DateTimeOffset.UtcNow.ToString("O"), fetched.ToString("O"), directory.LastUpdated, resultYear.ToString(CultureInfo.InvariantCulture), resultBen, f.Name,
                filings.Count > 0 ? "Filed" : "No certified filing found in published data", ((int)resultForm).ToString(CultureInfo.InvariantCulture), f.Number, filings.Count > 0 ? f.Status : "", f.Certified, f.Nickname, f.Services, f.Pdf,
                b?.Cycle ?? "Not reported", b?.Version ?? "Not reported", b is null ? "Not reported" : BudgetRow.ForecastView(b.Version, hasCycle470) ? "Forecast" : b.Version,
                b?.Total ?? "Not reported", b?.Committed ?? "Not reported", b?.Pending ?? "Not reported", b?.Available ?? "Not reported" });
            var path = Path.Combine(CacheDir!.AbsolutePath, "export-" + Guid.NewGuid().ToString("N") + "." + extension);
            await Task.Run(() => { if (extension == "csv") DataExporter.WriteCsv(path, rows); else DataExporter.WriteExcel(path, rows); }, ct);
            if (destroyed) { File.Delete(path); return; }
            pendingExport = path;
            try
            {
                var intent = new Intent(Intent.ActionCreateDocument);
                intent.AddCategory(Intent.CategoryOpenable);
                intent.SetType(extension == "csv" ? "text/csv" : "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet");
                intent.PutExtra(Intent.ExtraTitle, $"Form{(int)resultForm}_{resultBen}_FY{resultYear}.{extension}");
                StartActivityForResult(intent, SaveDocumentRequest);
            }
            catch { File.Delete(path); pendingExport = null; throw; }
        });
    }

    protected override async void OnActivityResult(int requestCode, Result resultCode, Intent? data)
    {
        base.OnActivityResult(requestCode, resultCode, data);
        if (requestCode != SaveDocumentRequest || pendingExport is null) return;
        var path = pendingExport; busy = true; UpdateControls();
        try
        {
            if (resultCode == Result.Ok && data?.Data is { } destination)
            {
                using var source = File.OpenRead(path);
                using var target = ContentResolver!.OpenOutputStream(destination, "wt") ?? throw new IOException("The selected location could not be opened.");
                await source.CopyToAsync(target);
                if (!destroyed) status.Text = "Export saved to the selected location.";
            }
            else if (!destroyed) status.Text = "Export canceled.";
        }
        catch (Exception ex) { if (!destroyed) Alert("Export failed: " + ex.Message); }
        finally { try { File.Delete(path); } catch (IOException) { } pendingExport = null; busy = false; if (!destroyed) UpdateControls(); }
    }

    protected override void OnSaveInstanceState(Bundle outState)
    {
        outState.PutString("name", name.Text); outState.PutString("ben", ben.Text); outState.PutString("year", year.Text);
        outState.PutInt("form", form.SelectedItemPosition); outState.PutString("pendingExport", pendingExport);
        base.OnSaveInstanceState(outState);
    }
    protected override void OnDestroy() { destroyed = true; operation?.Cancel(); http.Dispose(); base.OnDestroy(); }
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
        button.Click += async (_, _) => { try { await action(); } catch (Exception ex) { if (!destroyed) Alert(ex.Message); } };
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
