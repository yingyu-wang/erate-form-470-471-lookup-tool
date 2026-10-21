using System.Diagnostics;
using System.Globalization;
using System.Text;
using ERateLookup.Core;
namespace ERateLookup.Desktop;

public sealed class MainForm : Form
{
    private readonly TextBox customer = new() { Dock = DockStyle.Fill, PlaceholderText = "BEN or customer name" };
    private readonly TextBox state = new() { Width = 60, MaxLength = 2, CharacterCasing = CharacterCasing.Upper, PlaceholderText = "CA" };
    private readonly ComboBox formPicker = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 115 };
    private FilingForm selectedForm = FilingForm.Form470;
    private readonly ComboBox year = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 95 };
    private readonly Button search = new() { Text = "Search", AutoSize = true };
    private readonly Button refresh = new() { Text = "Refresh live", AutoSize = true };
    private readonly Label status = new() { AutoSize = true, MaximumSize = new Size(1000, 0), Padding = new Padding(0, 8, 0, 8) };
    private readonly ListBox matches = new() { Dock = DockStyle.Fill, Height = 100, SelectionMode = SelectionMode.MultiExtended };
    private readonly GroupBox matchGroup = new() { Text = "Choose a customer", Dock = DockStyle.Fill, Visible = false };
    private readonly Button choose = new() { Text = "Check selected customer", AutoSize = true };
    private readonly Button saveMatches = new() { Text = "Add selected BENs to watchlist", AutoSize = true, Enabled = false };
    private readonly Label matchSelection = new() { AutoSize = true, Margin = new Padding(8, 10, 8, 0) };
    private readonly DataGridView grid = new() { Dock = DockStyle.Fill, ReadOnly = true, AllowUserToAddRows = false, AllowUserToDeleteRows = false, MultiSelect = true, SelectionMode = DataGridViewSelectionMode.FullRowSelect, AutoGenerateColumns = false, RowHeadersVisible = false, BackgroundColor = Color.White, BorderStyle = BorderStyle.None };
    private readonly Label details = new() { AutoSize = true, MaximumSize = new Size(1000, 0) };
    private readonly Button open = new() { Text = "Open selected Form 470", AutoSize = true };
    private readonly Button export = new() { Text = "Export selected (CSV / Excel / JSON)", AutoSize = true };
    private readonly Button selectAll = new() { Text = "Select all", AutoSize = true, Enabled = false };
    private readonly Button unselectAll = new() { Text = "Unselect all", AutoSize = true, Enabled = false };
    private readonly Label selectionCount = new() { Text = "0 selected", AutoSize = true, Margin = new Padding(8, 10, 8, 0) };
    private readonly UsacClient client;
    private readonly CaliforniaDirectory directory;
    private readonly FilingWindow filingWindow;
    private readonly LookupViewModel screen;
    private readonly string portfolioDirectory;
    private string selectedName = "";
    private readonly Label windowBanner = new() { AutoSize = true, MaximumSize = new Size(1020, 0) };
    private readonly Button windowInfo = new() { Text = "Form 471 window info", AutoSize = true };
    private readonly System.Windows.Forms.Timer typingTimer = new() { Interval = 250 };
    private readonly Label budget = new GlassLabel() { AutoSize = true, MaximumSize = new Size(1020, 0), Padding = new Padding(16), Font = new Font("Segoe UI", 11, FontStyle.Bold) };
    private readonly Button syncDirectory = new() { Text = "Refresh CA directory / budgets", AutoSize = true };
    private readonly Button saveBen = new() { Text = "Add BEN to watchlist", AutoSize = true };
    private bool syncing;
    private bool? hasCycle470;
    private readonly ComboBox themePicker = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 135 };
    private string activeTheme = "Mint Glass";
    private string settingsPath = "";
    private Image? backgroundAsset;
    private bool lookupComplete;
    private DateTimeOffset lookupFetched;

    private readonly HttpClient http = new() { Timeout = TimeSpan.FromSeconds(30) };
    private readonly CancellationTokenSource lifetime = new();
    private readonly TableLayoutPanel layout;
    private List<Filing> results = [];
    private int selectedYear = 2027;
    private string selectedBen = "";
    private bool refreshRequested;
    private bool busy;
    public MainForm(string? dataDirectory = null)
    {
        using (var iconStream = System.Reflection.Assembly.GetExecutingAssembly().GetManifestResourceStream("ERateLookup.ico"))
            if (iconStream is not null) Icon = new Icon(iconStream);
        Text = "E-rate · Form 470 / 471 Lookup"; Width = 1160; Height = 760; MinimumSize = new Size(850, 550); StartPosition = FormStartPosition.CenterScreen; Font = new Font("Segoe UI", 10);
        BackColor = Color.FromArgb(232, 244, 240); DoubleBuffered = true;
        ForeColor = Color.FromArgb(23, 48, 44);
        http.DefaultRequestHeaders.UserAgent.ParseAdd("ERateLookup/2.0");
        portfolioDirectory = dataDirectory ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ERate470LookupDotNet");
        var cache = Path.Combine(portfolioDirectory, "cache");
        settingsPath = Path.Combine(Path.GetDirectoryName(cache)!, "theme.txt");
        using (var asset = System.Reflection.Assembly.GetExecutingAssembly().GetManifestResourceStream("mint-glass-background.png"))
            if (asset is not null) { using var loaded = Image.FromStream(asset); backgroundAsset = new Bitmap(loaded); }
        client = new(http, cache); filingWindow = new(http, cache); directory = new(http, cache); directory.LoadSeedIfEmpty(); screen = new(client, directory, filingWindow, LookupSurface.Windows); state.Text = "CA"; state.ReadOnly = true;
        for (var y = 2027; y >= 2016; y--) year.Items.Add(y); year.SelectedItem = 2027;
        layout = new() { BackColor = Color.Transparent, Dock = DockStyle.Fill, Padding = new Padding(24), ColumnCount = 1, RowCount = 10 }; Controls.Add(layout);
        layout.RowStyles.Add(new(SizeType.AutoSize)); layout.RowStyles.Add(new(SizeType.AutoSize)); layout.RowStyles.Add(new(SizeType.AutoSize)); layout.RowStyles.Add(new(SizeType.AutoSize)); layout.RowStyles.Add(new(SizeType.Absolute, 0)); layout.RowStyles.Add(new(SizeType.Percent, 100)); layout.RowStyles.Add(new(SizeType.AutoSize)); layout.RowStyles.Add(new(SizeType.AutoSize)); layout.RowStyles.Add(new(SizeType.AutoSize)); layout.RowStyles.Add(new(SizeType.AutoSize));
        var title = new ShadowLabel() { ForeColor = Color.FromArgb(16, 65, 56), BackColor = Color.Transparent, Text = "Has your customer filed?", Font = new Font("Segoe UI", 26, FontStyle.Bold), AutoSize = true, Margin = new Padding(0, 0, 0, 18) }; layout.Controls.Add(title, 0, 0);
        var inputs = new TableLayoutPanel() { Dock = DockStyle.Fill, ColumnCount = 6, RowCount = 2, AutoSize = true }; inputs.ColumnStyles.Add(new(SizeType.Percent, 100)); for (var i = 1; i < 6; i++) inputs.ColumnStyles.Add(new(SizeType.AutoSize));
        inputs.Controls.Add(new Label() { Text = "BEN or customer name", AutoSize = true }, 0, 0); inputs.Controls.Add(new Label() { Text = "Funding year", AutoSize = true }, 1, 0); inputs.Controls.Add(new Label() { Text = "State (optional)", AutoSize = true }, 2, 0);
        formPicker.Items.AddRange(new object[] { FilingForm.Form470, FilingForm.Form471 }); formPicker.Format += (_, e) => e.Value = $"Form {(int)(FilingForm)e.ListItem!}"; formPicker.FormattingEnabled = true; formPicker.SelectedIndex = 0;
        inputs.Controls.Add(new Label() { Text = "Form", AutoSize = true }, 3, 0); inputs.Controls.Add(formPicker, 3, 1);
        inputs.Controls.Add(customer, 0, 1); inputs.Controls.Add(year, 1, 1); inputs.Controls.Add(state, 2, 1); inputs.Controls.Add(search, 4, 1); inputs.Controls.Add(refresh, 5, 1); layout.Controls.Add(inputs, 0, 1); layout.Controls.Add(status, 0, 2);
        var matchActions = new FlowLayoutPanel { Dock = DockStyle.Bottom, AutoSize = true }; matchActions.Controls.Add(choose); matchActions.Controls.Add(saveMatches); matchActions.Controls.Add(matchSelection);
        matchGroup.Controls.Add(matches); matchGroup.Controls.Add(matchActions); layout.Controls.Add(budget, 0, 3); layout.Controls.Add(matchGroup, 0, 4); layout.Controls.Add(grid, 0, 5); layout.Controls.Add(details, 0, 6);
        foreach (var c in new[] { ("Number", "Form 470", 120), ("Certified", "Certified date", 115), ("Status", "Status", 145), ("Name", "Customer", 230), ("Services", "Requested services", 400) }) grid.Columns.Add(new DataGridViewTextBoxColumn() { Name = c.Item1, DataPropertyName = c.Item1, HeaderText = c.Item2, Width = c.Item3, AutoSizeMode = c.Item1 == "Services" ? DataGridViewAutoSizeColumnMode.Fill : DataGridViewAutoSizeColumnMode.None });
        var actions = new FlowLayoutPanel() { AutoSize = true, Dock = DockStyle.Fill };
        var portfolio = new Button { Text = "Saved BENs / Watchlists", AutoSize = true }; actions.Controls.Add(portfolio);
        portfolio.Click += (_, _) => { try { using var dialog = new PortfolioForm(client, Path.GetDirectoryName(cache)!, directory, filingWindow); dialog.ShowDialog(this); } catch (Exception ex) when (AppExceptions.IsOperational(ex)) { AppLog.Error(ex, "Portfolio"); MessageBox.Show(this, ex.Message, "Portfolio"); } };
        actions.Controls.Add(saveBen);
        saveBen.Click += (_, _) => AddBenToWatchlist(); actions.Controls.Add(open); actions.Controls.Add(selectAll); actions.Controls.Add(unselectAll); actions.Controls.Add(selectionCount); actions.Controls.Add(export); actions.Controls.Add(syncDirectory); actions.Controls.Add(windowInfo);
        actions.Controls.Add(new Label() { Text = "Theme", AutoSize = true, Margin = new Padding(15, 10, 5, 0) }); themePicker.Items.AddRange(new object[] { "Mint Glass", "Midnight", "Classic" }); actions.Controls.Add(themePicker); layout.Controls.Add(actions, 0, 7);
        layout.Controls.Add(new Label() { Text = "SQLite cache lasts 2 hours. Refresh live bypasses it. USAC publication may lag EPC submissions.\nNames search the local California entity directory as you type. Budget amounts are pre-discount; committed is not disbursed.", AutoSize = true, ForeColor = Color.DimGray, Margin = new Padding(0, 16, 0, 0) }, 0, 8);
        status.ForeColor = Color.FromArgb(33, 76, 66); status.Font = new Font("Segoe UI", 10, FontStyle.Bold); status.BackColor = Color.Transparent;
        grid.EnableHeadersVisualStyles = false; grid.ColumnHeadersDefaultCellStyle.BackColor = Color.FromArgb(222, 239, 234); grid.ColumnHeadersDefaultCellStyle.ForeColor = Color.FromArgb(20, 60, 50); grid.ColumnHeadersDefaultCellStyle.Font = new Font("Segoe UI", 10, FontStyle.Bold); grid.ColumnHeadersHeight = 36;
        grid.DefaultCellStyle.SelectionBackColor = Color.FromArgb(208, 232, 223); grid.DefaultCellStyle.SelectionForeColor = Color.FromArgb(14, 54, 43); grid.AlternatingRowsDefaultCellStyle.BackColor = Color.FromArgb(245, 250, 247); grid.GridColor = Color.FromArgb(225, 234, 229); grid.RowTemplate.Height = 32;
        foreach (var button in new[] { search, refresh, choose, open, export, selectAll, unselectAll, syncDirectory, windowInfo, saveBen, saveMatches })
        {
            button.FlatStyle = FlatStyle.Flat; button.FlatAppearance.BorderColor = Color.FromArgb(167, 198, 184); button.BackColor = Color.FromArgb(245, 252, 248); button.ForeColor = Color.FromArgb(24, 70, 55); button.Font = new Font("Segoe UI", 10, FontStyle.Bold); button.Padding = new Padding(8, 3, 8, 3);
        }
        search.BackColor = Color.FromArgb(30, 104, 82); search.ForeColor = Color.White;
        status.Text = "Enter a BEN or customer name. Funding year defaults to 2027.";
        search.Click += async (_, _) => await SearchAsync(false); refresh.Click += async (_, _) => await SearchAsync(true); choose.Click += async (_, _) => await ChooseAsync(); matches.DoubleClick += async (_, _) => await ChooseAsync();
        matches.SelectedIndexChanged += (_, _) => UpdateMatchSelection(); saveMatches.Click += (_, _) => AddMatchesToWatchlist();
        grid.SelectionChanged += (_, _) => { details.Text = SelectedFiling()?.Nickname ?? ""; UpdateSelection(); };
        selectAll.Click += (_, _) => grid.SelectAll(); unselectAll.Click += (_, _) => grid.ClearSelection(); grid.CellDoubleClick += (_, _) => OpenForm(); open.Click += (_, _) => OpenForm(); export.Click += (_, _) => Export(); AcceptButton = search;
        customer.TextChanged += (_, _) => { typingTimer.Stop(); if (!busy) { ResetLookup("Customer changed. Search again."); typingTimer.Start(); } };
        typingTimer.Tick += (_, _) => { typingTimer.Stop(); Suggest(); };
        year.SelectedIndexChanged += async (_, _) => { ResetLookup("Funding year changed. Search again."); await ShowWindowNoticeAsync(true); };
        formPicker.SelectedIndexChanged += async (_, _) =>
        {
            selectedForm = (FilingForm)formPicker.SelectedItem!;
            grid.Columns["Number"]!.HeaderText = $"Form {(int)selectedForm}";
            grid.Columns["Services"]!.HeaderText = selectedForm == FilingForm.Form471 ? "Service category" : "Requested services";
            open.Text = $"Open selected Form {(int)selectedForm}";
            ResetLookup("Form type changed. Search again."); await ShowWindowNoticeAsync(true);
        };
        syncDirectory.Click += async (_, _) => await SyncDirectoryAsync(true);
        layout.Controls.Add(windowBanner, 0, 9); windowInfo.Click += async (_, _) => await ShowWindowNoticeAsync(true);
        Shown += async (_, _) => { budget.Text = "California directory / C2 data snapshot: " + directory.LastUpdated; if (directory.NeedsRefresh) await SyncDirectoryAsync(false); };
        themePicker.SelectedIndexChanged += (_, _) => ApplyTheme(themePicker.SelectedItem as string ?? "Mint Glass", true);
        var saved = "Mint Glass"; try { if (File.Exists(settingsPath)) saved = File.ReadAllText(settingsPath).Trim(); } catch (IOException) { } catch (UnauthorizedAccessException) { }
        themePicker.SelectedItem = themePicker.Items.Contains(saved) ? saved : "Mint Glass";
        FormClosing += (_, _) => { typingTimer.Stop(); lifetime.Cancel(); }; FormClosed += (_, _) => ReleaseRuntime(); UpdateSelection();
    }
    int runtimeReleased;
    void ReleaseRuntime()
    {
        if (Interlocked.Exchange(ref runtimeReleased, 1) != 0) return;
        typingTimer.Stop();
        backgroundAsset?.Dispose();
        typingTimer.Dispose();
        client.Dispose();
        http.Dispose();
        lifetime.Cancel();
        lifetime.Dispose();
    }
    protected override void Dispose(bool disposing)
    {
        if (disposing) ReleaseRuntime();
        base.Dispose(disposing);
    }
    protected override void OnPaintBackground(PaintEventArgs e)
    {
        if (activeTheme == "Mint Glass" && backgroundAsset is not null) { e.Graphics.DrawImage(backgroundAsset, ClientRectangle); return; }
        if (activeTheme == "Classic") { e.Graphics.Clear(Color.FromArgb(245, 246, 247)); return; }
        using var gradient = new System.Drawing.Drawing2D.LinearGradientBrush(ClientRectangle, Color.FromArgb(26, 42, 54), Color.FromArgb(13, 23, 33), System.Drawing.Drawing2D.LinearGradientMode.ForwardDiagonal);
        e.Graphics.FillRectangle(gradient, ClientRectangle);
    }
    private void ApplyTheme(string name, bool save)
    {
        activeTheme = name; var dark = name == "Midnight"; var classic = name == "Classic";
        var fg = dark ? Color.FromArgb(232, 242, 248) : Color.FromArgb(22, 60, 50); var bg = dark ? Color.FromArgb(26, 42, 54) : Color.FromArgb(245, 249, 247);
        void Style(Control parent)
        {
            foreach (Control c in parent.Controls)
            {
                if (c is Label l) { l.ForeColor = fg; l.BackColor = Color.Transparent; }
                if (c is GlassLabel card) { card.Dark = dark; card.Classic = classic; card.ForeColor = fg; card.Invalidate(); }
                if (c is TextBox or ComboBox or ListBox) { c.BackColor = dark ? Color.FromArgb(36, 53, 65) : Color.White; c.ForeColor = fg; }
                if (c is Button btn) { btn.BackColor = dark ? Color.FromArgb(47, 67, 78) : Color.FromArgb(245, 252, 248); btn.ForeColor = fg; btn.FlatAppearance.BorderColor = dark ? Color.FromArgb(82, 110, 124) : Color.FromArgb(167, 198, 184); }
                if (c is GroupBox) { c.ForeColor = fg; c.BackColor = bg; }
                if (c is FlowLayoutPanel or TableLayoutPanel) c.BackColor = Color.Transparent;
                Style(c);
            }
        }
        Style(this); search.BackColor = dark ? Color.FromArgb(39, 120, 111) : Color.FromArgb(30, 104, 82); search.ForeColor = Color.White;
        grid.BackgroundColor = dark ? Color.FromArgb(21, 34, 43) : Color.White;
        grid.DefaultCellStyle.BackColor = dark ? Color.FromArgb(27, 42, 53) : Color.White; grid.DefaultCellStyle.ForeColor = fg;
        grid.AlternatingRowsDefaultCellStyle.BackColor = dark ? Color.FromArgb(32, 48, 59) : Color.FromArgb(245, 250, 247);
        grid.ColumnHeadersDefaultCellStyle.BackColor = dark ? Color.FromArgb(42, 62, 73) : Color.FromArgb(222, 239, 234); grid.ColumnHeadersDefaultCellStyle.ForeColor = fg;
        grid.DefaultCellStyle.SelectionBackColor = dark ? Color.FromArgb(48, 95, 100) : Color.FromArgb(208, 232, 223); grid.DefaultCellStyle.SelectionForeColor = fg;
        grid.GridColor = dark ? Color.FromArgb(47, 65, 76) : Color.FromArgb(225, 234, 229);
        Invalidate(true);
        if (save) { try { File.WriteAllText(settingsPath, name); } catch (IOException) { } catch (UnauthorizedAccessException) { } }
    }
    private async Task ShowWindowNoticeAsync(bool popup)
    {
        if (selectedForm != FilingForm.Form471) { windowBanner.Text = ""; return; }
        var requestedYear = (int)year.SelectedItem!;
        windowBanner.Text = $"Checking official FY{requestedYear} Form 471 window information…";
        var notice = await screen.WindowBannerAsync(requestedYear, selectedForm, lifetime.Token);
        if (IsDisposed || selectedForm != FilingForm.Form471 || (int)year.SelectedItem! != requestedYear) return;
        if (notice.Text.Length == 0) return;
        windowBanner.Text = notice.Text;
        if (popup && notice.PromptToOpen && notice.SourceUrl is not null && MessageBox.Show(this, windowBanner.Text + "\n\nOpen USAC's official page?", "Form 471 filing window", MessageBoxButtons.YesNo, MessageBoxIcon.Information) == DialogResult.Yes)
            try { Process.Start(new ProcessStartInfo(notice.SourceUrl) { UseShellExecute = true }); }
            catch (Exception ex) when (AppExceptions.IsOperational(ex)) { AppLog.Error(ex, "Open filing window page"); MessageBox.Show(this, "Could not open the page: " + ex.Message); }
    }
    private void ResetLookup(string message)
    {
        if (busy) return;
        grid.DataSource = null; results = []; lookupComplete = false; details.Text = ""; selectedBen = ""; selectedName = ""; hasCycle470 = null;
        matches.DataSource = null; budget.Text = "Select a customer or enter a BEN to view its C2 budget."; status.Text = message; ShowMatches(false); UpdateSelection();
    }
    private void UpdateSelection()
    {
        var count = grid.SelectedRows.Count;
        selectionCount.Text = $"{count} of {results.Count} selected";
        selectAll.Enabled = unselectAll.Enabled = !busy && lookupComplete && results.Count > 0;
        saveBen.Enabled = !busy && selectedBen.Length > 0;
    }
    private void UpdateMatchSelection()
    { var count = matches.SelectedItems.Count; matchSelection.Text = $"{count} selected · Ctrl/Shift for multiple"; saveMatches.Enabled = !busy && matchGroup.Visible && count > 0; choose.Enabled = !busy && matchGroup.Visible && count == 1; }
    private void SetBusy(bool value) { busy = value; search.Enabled = refresh.Enabled = customer.Enabled = state.Enabled = year.Enabled = formPicker.Enabled = open.Enabled = export.Enabled = matches.Enabled = !value; UseWaitCursor = value; UpdateSelection(); UpdateMatchSelection(); }
    private void ShowMatches(bool value) { matchGroup.Visible = value; layout.RowStyles[4].Height = value ? 175 : 0; UpdateMatchSelection(); }
    private async Task ExecuteAsync(Func<Task> action)
    {
        if (busy) return; SetBusy(true);
        try { await action(); }
        catch (OperationCanceledException) { if (!IsDisposed && !lifetime.IsCancellationRequested) status.Text = "Lookup timed out. No filing determination was made; try again."; }
        catch (Exception ex) when (AppExceptions.IsOperational(ex)) { AppLog.Error(ex, "Filing lookup"); if (!IsDisposed) status.Text = "Lookup failed — no filing determination was made. " + ex.Message; }
        finally { if (!IsDisposed) SetBusy(false); }
    }
    private async Task SearchAsync(bool force)
    {
        if (busy) return;
        var problem = screen.ValidateCustomerQuery(customer.Text, state.Text);
        if (problem is not null) { MessageBox.Show(this, problem); return; }
        var text = customer.Text.Trim();
        selectedYear = (int)year.SelectedItem!; refreshRequested = force; typingTimer.Stop(); ResetLookup("Searching…");
        await ExecuteAsync(async () =>
        {
            if (LookupViewModel.IsBen(text)) { await LookupAsync(text); return; }
            var found = screen.SearchByName(text);
            matches.DataSource = found.Matches.ToList();
            if (!found.Visible) { status.Text = found.Status; return; }
            ShowMatches(true); status.Text = found.Status;
        });
    }
    private async Task ChooseAsync()
    {
        if (busy || matches.SelectedItems.Count != 1 || matches.SelectedItem is not Entity entity) return;
        selectedYear = (int)year.SelectedItem!; ShowMatches(false); await ExecuteAsync(() => LookupAsync(entity.Ben, entity.Name));
    }
    private async Task LookupAsync(string ben, string name = "")
    {
        lookupComplete = false; results = []; grid.DataSource = null; details.Text = ""; selectedBen = ben; selectedName = name; hasCycle470 = null; ShowBudget(ben); status.Text = $"Checking BEN {ben}, funding year {selectedYear}…";
        var outcome = await screen.LookupAsync(ben, selectedYear, selectedForm, refreshRequested, name, lifetime.Token);
        results = outcome.Filings.ToList(); lookupComplete = outcome.Complete; lookupFetched = outcome.FetchedAt; hasCycle470 = outcome.HasNewCycleForm470; grid.DataSource = results; grid.SelectAll(); UpdateSelection();
        ShowBudget(ben); status.Text = outcome.Status;
    }
    private void Suggest()
    {
        if (busy) return; refreshRequested = false; selectedYear = (int)year.SelectedItem!;
        var found = screen.Suggest(customer.Text);
        if (found.Status is null) { ShowMatches(false); return; }
        matches.DataSource = found.Matches.ToList(); ShowMatches(found.Visible); status.Text = found.Status;
    }
    private void ShowBudget(string ben)
    {
        budget.Text = screen.BudgetText(ben, selectedYear, hasCycle470);
    }
    private async Task SyncDirectoryAsync(bool manual)
    {
        if (syncing) return; syncing = true; syncDirectory.Enabled = false;
        try { await directory.RefreshAsync(lifetime.Token); if (!IsDisposed) { if (selectedBen.Length > 0) ShowBudget(selectedBen); else budget.Text = "California directory and C2 budget data refreshed: " + directory.LastUpdated; if (manual && !busy) status.Text = "California customer directory and budgets refreshed successfully."; } }
        catch (OperationCanceledException ex) when (!lifetime.IsCancellationRequested) { AppLog.Error(ex, "Directory refresh"); if (!IsDisposed) budget.Text = "Directory refresh timed out; previous local data retained. Last update: " + directory.LastUpdated; }
        catch (Exception ex) when (AppExceptions.IsOperational(ex)) { AppLog.Error(ex, "Directory refresh"); if (!IsDisposed) budget.Text = "Directory refresh failed; previous local data retained. Last update: " + directory.LastUpdated + "\n" + ex.Message; }
        finally { syncing = false; if (!IsDisposed) syncDirectory.Enabled = true; }
    }
    private void AddBenToWatchlist()
    {
        if (selectedBen.Length == 0) { MessageBox.Show(this, "Identify a BEN first — search by name or enter a BEN, then run a lookup."); return; }
        try
        {
            var portfolioStore = new PortfolioStore(portfolioDirectory);
            if (portfolioStore.Lists().Count == 0) { MessageBox.Show(this, "Create a watchlist in Saved BENs / Watchlists first."); return; }
            var suggested = LookupViewModel.SuggestedWatchlistName(selectedName, directory.GetBudget(selectedBen, selectedYear)?.Name);
            using var dialog = new AddToWatchlistDialog(portfolioStore, selectedBen, suggested);
            if (dialog.ShowDialog(this) == DialogResult.OK) status.Text = $"BEN {selectedBen} saved to \"{dialog.ChosenListName}\". Open Saved BENs / Watchlists to refresh it.";
        }
        catch (Exception ex) when (AppExceptions.IsOperational(ex)) { AppLog.Error(ex, "Add BEN to watchlist"); MessageBox.Show(this, ex.Message, "Add BEN to watchlist"); }
    }
    private void AddMatchesToWatchlist()
    {
        if (busy || !matchGroup.Visible) return;
        var entries = matches.SelectedItems.Cast<Entity>().DistinctBy(e => e.Ben).Select(e => new WatchlistEntry(e.Ben, e.Name, "")).ToList();
        if (entries.Count == 0) return;
        typingTimer.Stop();
        try
        {
            var store = new PortfolioStore(portfolioDirectory);
            if (store.Lists().Count == 0) { MessageBox.Show(this, "Create a watchlist in Saved BENs / Watchlists first."); return; }
            using var dialog = new AddToWatchlistDialog(store, entries);
            if (dialog.ShowDialog(this) == DialogResult.OK) status.Text = $"{dialog.AddedCount} BEN(s) added to \"{dialog.ChosenListName}\"; {dialog.SkippedCount} already saved. No filing lookup was needed.";
        }
        catch (Exception ex) when (AppExceptions.IsOperational(ex)) { AppLog.Error(ex, "Add selected BENs to watchlist"); MessageBox.Show(this, ex.Message, "Add selected BENs to watchlist"); }
    }
    private Filing? SelectedFiling() => grid.CurrentRow?.DataBoundItem as Filing;
    private void OpenForm()
    {
        var filing = SelectedFiling(); if (filing is null) { MessageBox.Show(this, "Select a filing first."); return; }
        if (!Uri.TryCreate(filing.Pdf, UriKind.Absolute, out var uri) || uri.Scheme != "https") { MessageBox.Show(this, "USAC did not provide a usable PDF link."); return; }
        try { Process.Start(new ProcessStartInfo(uri.AbsoluteUri) { UseShellExecute = true }); }
        catch (Exception ex) when (AppExceptions.IsOperational(ex)) { AppLog.Error(ex, "Open filing PDF"); MessageBox.Show(this, "Could not open the PDF: " + ex.Message); }
    }
    private void Export()
    {
        if (!lookupComplete) { MessageBox.Show(this, "Complete a lookup before exporting."); return; }
        // Keep the original results order; Ctrl-click order does not reorder the export.
        var selectedNumbers = grid.SelectedRows.Cast<DataGridViewRow>().Select(r => (r.DataBoundItem as Filing)?.Number).OfType<string>().ToHashSet();
        var selectedResults = results.Where(r => selectedNumbers.Contains(r.Number)).ToList();
        if (results.Count > 0 && selectedResults.Count == 0) { MessageBox.Show(this, "Select at least one filing to export. Use Ctrl-click or Select all."); return; }
        using var dialog = new SaveFileDialog() { Filter = "CSV files (*.csv)|*.csv|Excel workbook (*.xlsx)|*.xlsx|JSON (*.json)|*.json", FileName = $"Form{(int)selectedForm}_{selectedBen}_FY{selectedYear}", DefaultExt = "csv", AddExtension = true }; if (dialog.ShowDialog(this) != DialogResult.OK) return;
        try
        {
            var prepared = screen.CreateExport(selectedYear, selectedForm, selectedBen, customer.Text, lookupFetched, results, selectedNumbers, hasCycle470);
            var path = DataExporter.ApplySaveFilterExtension(dialog.FileName, dialog.FilterIndex, ".csv", ".xlsx", ".json");
            prepared.Write(path);
            status.Text = $"Exported {selectedResults.Count} selected filing(s) to " + path;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException) { AppLog.Error(ex, "Export filings"); MessageBox.Show(this, "Export failed: " + ex.Message); }
    }
}
