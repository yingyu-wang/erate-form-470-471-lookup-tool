using ERateLookup.Core;

namespace ERateLookup.Desktop;

public sealed class PortfolioForm : Form
{
    readonly PortfolioSession session;
    readonly CaliforniaDirectory directory;
    readonly FilingWindow windows;
    readonly ComboBox lists = new() { Width = 220, DropDownStyle = ComboBoxStyle.DropDownList };
    readonly ListBox entries = new() { Width = 260, Height = 100 };
    readonly TextBox listName = new() { Width = 150, PlaceholderText = "List name" }, ben = new() { Width = 100, PlaceholderText = "BEN" }, name = new() { Width = 180, PlaceholderText = "Display name" }, tags = new() { Width = 150, PlaceholderText = "Tags" };
    readonly TextBox years = new() { Width = 150, Text = (DateTime.Now.Year + 1).ToString() }, keyword = new() { Width = 180, PlaceholderText = "Keyword" }, service = new() { Width = 180, PlaceholderText = "Service contains" };
    readonly ComboBox forms = new() { Width = 110, DropDownStyle = ComboBoxStyle.DropDownList };
    readonly CheckBox changes = new() { Text = "Changes only", AutoSize = true };
    readonly CheckedListBox results = new() { Dock = DockStyle.Fill, CheckOnClick = true, HorizontalScrollbar = true };
    readonly Label status = new() { AutoSize = true, MaximumSize = new(1050, 0) }, counts = new() { AutoSize = true };
    readonly List<Control> gated = [];
    List<PortfolioRow> visible = [];
    CancellationTokenSource? operation;
    bool rendering;
    int page;
    string? ListId => (lists.SelectedItem as Watchlist)?.Id;

    public PortfolioForm(UsacClient client, string dataDirectory, CaliforniaDirectory directory, FilingWindow windows)
    {
        session = new(client, new PortfolioStore(dataDirectory)); this.directory = directory; this.windows = windows;
        Text = "Saved BENs / Watchlists"; Width = 1150; Height = 780; MinimumSize = new(850, 600);
        var layout = new TableLayoutPanel { Dock = DockStyle.Fill, Padding = new(16), ColumnCount = 1, RowCount = 7 }; Controls.Add(layout);
        for (var i = 0; i < 7; i++) layout.RowStyles.Add(new(i == 4 ? SizeType.Percent : SizeType.AutoSize, i == 4 ? 100 : 0));
        FlowLayoutPanel Row(params Control[] controls) { var row = new FlowLayoutPanel { Dock = DockStyle.Fill, AutoSize = true }; row.Controls.AddRange(controls); return row; }
        Button Action(string title, Func<Task> action, bool gate = true) { var b = new Button { Text = title, AutoSize = true }; b.Click += async (_, _) => { if (gate) await Run(action); else await action(); }; if (gate) gated.Add(b); return b; }
        Task Do(Action action) { action(); return Task.CompletedTask; }
        layout.Controls.Add(Row(lists, listName,
            Action("New list", () => Do(() => { var list = session.Store.CreateList(listName.Text); ReloadLists(list.Id); })),
            Action("Rename list", () => Do(() => { session.Store.RenameList(RequireList(), listName.Text); ReloadLists(ListId); })),
            Action("Delete list", () => Do(() => { if (MessageBox.Show(this, "Delete this watchlist? Saved filing history is retained.", "Delete list", MessageBoxButtons.YesNo) == DialogResult.Yes) { session.Store.DeleteList(RequireList()); ReloadLists(); } }))), 0, 0);
        layout.Controls.Add(Row(entries, ben, name, tags,
            Action("Save BEN", () => Do(() => { session.Store.SaveEntry(RequireList(), new(ben.Text, name.Text, tags.Text)); ReloadEntries(); })),
            Action("Update BEN", () => Do(() => { session.Store.SaveEntry(RequireList(), new(ben.Text, name.Text, tags.Text), true); ReloadEntries(); })),
            Action("Remove BEN", () => Do(() => { session.Store.RemoveEntry(RequireList(), ben.Text.Trim()); ReloadEntries(); })),
            Action("Import BEN CSV", ImportAsync)), 0, 1);
        forms.Items.AddRange(new object[] { "Form 470", "Form 471", "Both forms" }); forms.SelectedIndex = 0;
        var cancel = Action("Cancel", () => Do(() => operation?.Cancel()), false);
        layout.Controls.Add(Row(new Label { Text = "Years (comma separated)", AutoSize = true }, years, forms,
            Action("Refresh watchlist", () => RefreshAsync(false)), Action("Refresh selected BEN", () => RefreshAsync(true)),
            Action("Open saved / offline", () => Do(() => { session.LoadSaved(Queries(false)); page = 0; Render(); status.Text = "Saved results; no network request made."; })), cancel), 0, 2);
        layout.Controls.Add(Row(keyword, service, changes, Action("Apply filters", () => Do(() => { page = 0; Render(); })),
            Action("Select visible", () => Do(() => { foreach (var r in visible) session.Selected.Add(r.Id); Render(); })),
            Action("Clear selection", () => Do(() => { session.Selected.Clear(); Render(); })), counts), 0, 3);
        layout.Controls.Add(results, 0, 4);
        layout.Controls.Add(Row(Action("Previous", () => Do(() => { page = Math.Max(0, page - 1); Render(); })), Action("Next", () => Do(() => { page++; Render(); })),
            Action("Change history", () => Do(() => ShowText(session.ChangeHistory(), "Unread changes"))), Action("Mark changes read", () => Do(() => { session.AcknowledgeDisplayed(); Render(); })), Action("Export selected", ExportAsync), Action("Deadlines", async () => { var text = await PortfolioInsights.DeadlinesAsync(session, windows, operation!.Token); if (!IsDisposed) ShowText(text, "Source-backed dates"); }), Action("History / summary", () => Do(() => ShowText(PortfolioInsights.Summary(session, directory), "District history / summary"))),
            Action("Open selected PDF", () => Do(() => { if (results.SelectedItem is PortfolioRow r && Uri.TryCreate(r.Filing?.Pdf, UriKind.Absolute, out var uri) && uri.Scheme == "https") System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(uri.AbsoluteUri) { UseShellExecute = true }); else throw new InvalidOperationException("Select an application with an HTTPS document link."); })),
            Action("Clear saved history", () => Do(() => { if (MessageBox.Show(this, "Delete all saved filing snapshots? Watchlists are retained.", "Clear history", MessageBoxButtons.YesNo) == DialogResult.Yes) { session.Store.ClearHistory(); session.ClearView(); Render(); } }))), 0, 5);
        layout.Controls.Add(status, 0, 6);
        gated.AddRange(new Control[] { lists, listName, entries, ben, name, tags, years, forms, keyword, service, changes, results });
        lists.SelectedIndexChanged += (_, _) => { listName.Text = (lists.SelectedItem as Watchlist)?.Name ?? ""; ReloadEntries(); };
        entries.SelectedIndexChanged += (_, _) => { if (entries.SelectedItem is WatchlistEntry e) { ben.Text = e.Ben; name.Text = e.Name; tags.Text = e.Tags; } };
        results.ItemCheck += (_, e) => { if (rendering) return; var r = (PortfolioRow)results.Items[e.Index]; if (e.NewValue == CheckState.Checked) session.Selected.Add(r.Id); else session.Selected.Remove(r.Id); counts.Text = $"{session.Selected.Count} selected"; };
        FormClosing += (_, _) => operation?.Cancel(); ReloadLists();
        status.Text = "Save BENs, choose years/forms, then refresh. Open saved works without a connection. CSV requires BEN; Name and Tags are optional.";
    }
    string RequireList() => ListId ?? throw new InvalidOperationException("Create or select a watchlist first.");
    void ReloadLists(string? selected = null) { var items = session.Store.Lists(); lists.DataSource = items; lists.SelectedItem = items.FirstOrDefault(l => l.Id == selected) ?? items.FirstOrDefault(); ReloadEntries(); }
    void ReloadEntries() { entries.DataSource = ListId is null ? new List<WatchlistEntry>() : session.Store.Entries(ListId); }
    List<LookupKey> Queries(bool single) => PortfolioSession.Queries(single ? entries.SelectedItem is WatchlistEntry e ? new[] { e } : throw new InvalidOperationException("Select a saved BEN first.") : session.Store.Entries(RequireList()), PortfolioSession.ParseYears(years.Text), forms.SelectedIndex);
    async Task Run(Func<Task> action)
    {
        if (operation is not null) return; using var current = new CancellationTokenSource(); operation = current; foreach (var c in gated) c.Enabled = false;
        try { await action(); }
        catch (OperationCanceledException) { if (!IsDisposed) status.Text = "Canceled. A selected export destination may contain an incomplete file."; }
        catch (Exception ex) when (AppExceptions.IsOperational(ex)) { AppLog.Error(ex, "Portfolio"); if (!IsDisposed) status.Text = ex.Message; }
        finally { operation = null; if (!IsDisposed) foreach (var c in gated) c.Enabled = true; }
    }
    async Task RefreshAsync(bool single)
    {
        var queries = Queries(single); var progress = new Progress<string>(s => { if (!IsDisposed) status.Text = s; });
        await Task.Run(() => session.RefreshAsync(queries, progress, operation!.Token)); if (!IsDisposed) { page = 0; Render(); }
    }
    void Render()
    {
        visible = session.Filter(keyword.Text, service.Text, changes.Checked); page = Math.Clamp(page, 0, Math.Max(0, (visible.Count - 1) / 50)); rendering = true;
        try { results.Items.Clear(); foreach (var r in visible.Skip(page * 50).Take(50)) results.Items.Add(r, session.Selected.Contains(r.Id)); }
        finally { rendering = false; }
        counts.Text = $"{session.Selected.Count} selected · {visible.Count}/{session.Rows.Count} visible · page {page + 1}/{Math.Max(1, (visible.Count + 49) / 50)}";
    }
    void ShowText(string message, string title)
    {
        using var dialog = new Form { Text = title, Width = 850, Height = 650, StartPosition = FormStartPosition.CenterParent };
        dialog.Controls.Add(new TextBox { Multiline = true, ReadOnly = true, ScrollBars = ScrollBars.Both, WordWrap = false, Dock = DockStyle.Fill, Text = message.Replace("\n", Environment.NewLine) }); dialog.ShowDialog(this);
    }
    async Task ImportAsync()
    {
        var id = RequireList(); using var dialog = new OpenFileDialog { Filter = "BEN CSV (*.csv)|*.csv" }; if (dialog.ShowDialog(this) != DialogResult.OK) return;
        if (new FileInfo(dialog.FileName).Length > 1024 * 1024) throw new ArgumentException("CSV exceeds 1 MiB.");
        using var input = File.OpenRead(dialog.FileName);
        var preview = await PortfolioSession.ReadCsvAsync(input, operation!.Token); operation.Token.ThrowIfCancellationRequested();
        if (MessageBox.Show(this, $"Import {preview.Entries.Count} valid BENs?\n{string.Join("\n", preview.Errors.Take(10))}\nExisting BENs will be skipped. Extra columns are ignored.", "Import preview", MessageBoxButtons.YesNo) != DialogResult.Yes) return;
        operation!.Token.ThrowIfCancellationRequested();
        var added = await Task.Run(() => session.Store.ImportEntries(id, preview.Entries, operation.Token));
        if (!IsDisposed) { ReloadEntries(); status.Text = $"Imported {added} BENs."; }
    }
    async Task ExportAsync()
    {
        var prepared = session.CreateExport(directory); using var dialog = new SaveFileDialog { Filter = "Excel (*.xlsx)|*.xlsx|CSV (*.csv)|*.csv|JSON (*.json)|*.json", FileName = "E-rate-portfolio.xlsx" }; if (dialog.ShowDialog(this) != DialogResult.OK) return;
        var path = DataExporter.ApplySaveFilterExtension(dialog.FileName, dialog.FilterIndex, ".xlsx", ".csv", ".json");
        await Task.Run(() => prepared.Write(path, operation!.Token)); status.Text = $"Exported {prepared.Table.Count - 1} selected results.";
    }
}
