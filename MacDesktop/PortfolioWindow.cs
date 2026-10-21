using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Platform.Storage;
using Avalonia.Media;
using ERateLookup.Core;

namespace ERateLookup.MacDesktop;

public sealed class PortfolioWindow : Window
{
    readonly PortfolioSession session;
    readonly CaliforniaDirectory directory;
    readonly FilingWindow windows;
    readonly ComboBox lists = new() { Width = 220 }, forms = new() { Width = 140 };
    readonly ListBox entries = new() { Width = 260, Height = 110 };
    readonly TextBox listName = Field("List name", 150), ben = Field("BEN", 100), name = Field("Display name", 180), tags = Field("Tags", 160);
    readonly TextBox years = Field("Years, comma separated", 180), keyword = Field("Keyword", 180), service = Field("Service contains", 180);
    readonly CheckBox changes = new() { Content = "Changes only" };
    readonly StackPanel results = new() { Spacing = 8 };
    readonly TextBlock status = Text("Save BENs, choose years/forms, then refresh. Open saved works offline."), counts = Text("");
    readonly List<Control> gated = [];
    List<PortfolioRow> visible = [];
    CancellationTokenSource? operation;
    bool closed;
    int page;
    string? ListId => (lists.SelectedItem as Watchlist)?.Id;

    public PortfolioWindow(UsacClient client, string dataDirectory, CaliforniaDirectory directory, FilingWindow windows)
    {
        session = new(client, new PortfolioStore(dataDirectory)); this.directory = directory; this.windows = windows; Title = "Saved BENs / Watchlists"; Width = 1150; Height = 820; MinWidth = 850; MinHeight = 600;
        var root = new Grid { RowDefinitions = new("Auto,Auto,Auto,Auto,*,Auto,Auto"), Margin = new(18) }; Content = root;
        void Add(Control c, int row) { Grid.SetRow(c, row); root.Children.Add(c); }
        Button Action(string title, Func<Task> action, bool gate = true) { var b = new Button { Content = title, Padding = new(10, 6), Margin = new(0, 0, 6, 6) }; b.Click += async (_, _) => { if (gate) await Run(action); else await action(); }; if (gate) gated.Add(b); return b; }
        Task Do(Action action) { action(); return Task.CompletedTask; }
        Add(Row(lists, listName, Action("New list", () => Do(() => { var list = session.Store.CreateList(listName.Text ?? ""); ReloadLists(list.Id); })),
            Action("Rename list", () => Do(() => { session.Store.RenameList(RequireList(), listName.Text ?? ""); ReloadLists(ListId); })),
            Action("Delete list", async () => { if (await Confirm("Delete this watchlist? Saved filing history is retained.")) { session.Store.DeleteList(RequireList()); ReloadLists(); } })), 0);
        Add(Row(entries, ben, name, tags, Action("Save BEN", () => Do(() => { session.Store.SaveEntry(RequireList(), new(ben.Text ?? "", name.Text ?? "", tags.Text ?? "")); ReloadEntries(); })),
            Action("Update BEN", () => Do(() => { session.Store.SaveEntry(RequireList(), new(ben.Text ?? "", name.Text ?? "", tags.Text ?? ""), true); ReloadEntries(); })),
            Action("Remove BEN", () => Do(() => { session.Store.RemoveEntry(RequireList(), ben.Text?.Trim() ?? ""); ReloadEntries(); })), Action("Import BEN CSV", ImportAsync)), 1);
        forms.ItemsSource = new[] { "Form 470", "Form 471", "Both forms" }; forms.SelectedIndex = 0; years.Text = (DateTime.Now.Year + 1).ToString();
        Add(Row(Text("Years"), years, forms, Action("Refresh watchlist", () => RefreshAsync(false)), Action("Refresh selected BEN", () => RefreshAsync(true)),
            Action("Open saved / offline", () => Do(() => { session.LoadSaved(Queries(false)); page = 0; Render(); status.Text = "Saved results; no network request made."; })),
            Action("Cancel", () => Do(() => operation?.Cancel()), false)), 2);
        Add(Row(keyword, service, changes, Action("Apply filters", () => Do(() => { page = 0; Render(); })), Action("Select visible", () => Do(() => { foreach (var r in visible) session.Selected.Add(r.Id); Render(); })),
            Action("Clear selection", () => Do(() => { session.Selected.Clear(); Render(); })), counts), 3);
        Add(new ScrollViewer { Content = results }, 4);
        Add(Row(Action("Previous", () => Do(() => { page = Math.Max(0, page - 1); Render(); })), Action("Next", () => Do(() => { page++; Render(); })), Action("Change history", () => ShowText(session.ChangeHistory())), Action("Mark changes read", () => Do(() => { session.AcknowledgeDisplayed(); Render(); })), Action("Export selected", ExportAsync), Action("Deadlines", async () => { var text = await PortfolioInsights.DeadlinesAsync(session, windows, operation!.Token); if (!closed) await ShowText(text); }),
            Action("History / summary", () => ShowText(PortfolioInsights.Summary(session, directory))), Action("Clear saved history", async () => { if (await Confirm("Delete all saved filing snapshots? Watchlists are retained.")) { session.Store.ClearHistory(); session.ClearView(); Render(); } })), 5);
        Add(status, 6); gated.AddRange(new Control[] { lists, listName, entries, ben, name, tags, years, forms, keyword, service, changes, results });
        lists.SelectionChanged += (_, _) => { listName.Text = (lists.SelectedItem as Watchlist)?.Name ?? ""; ReloadEntries(); };
        entries.SelectionChanged += (_, _) => { if (entries.SelectedItem is WatchlistEntry e) { ben.Text = e.Ben; name.Text = e.Name; tags.Text = e.Tags; } };
        Closed += (_, _) => { closed = true; operation?.Cancel(); }; ReloadLists();
    }
    static TextBox Field(string hint, double width) => new() { PlaceholderText = hint, Width = width, Margin = new(0, 0, 6, 6) };
    static TextBlock Text(string text) => new() { Text = text, TextWrapping = TextWrapping.Wrap, Margin = new(0, 4, 6, 6) };
    static WrapPanel Row(params Control[] controls) { var row = new WrapPanel(); foreach (var c in controls) row.Children.Add(c); return row; }
    string RequireList() => ListId ?? throw new InvalidOperationException("Create or select a watchlist first.");
    void ReloadLists(string? selected = null) { var items = session.Store.Lists(); lists.ItemsSource = items; lists.SelectedItem = items.FirstOrDefault(l => l.Id == selected) ?? items.FirstOrDefault(); ReloadEntries(); }
    void ReloadEntries() => entries.ItemsSource = ListId is null ? new List<WatchlistEntry>() : session.Store.Entries(ListId);
    List<LookupKey> Queries(bool single) => PortfolioSession.Queries(single ? entries.SelectedItem is WatchlistEntry e ? new[] { e } : throw new InvalidOperationException("Select a saved BEN first.") : session.Store.Entries(RequireList()), PortfolioSession.ParseYears(years.Text ?? ""), forms.SelectedIndex);
    async Task Run(Func<Task> action)
    {
        if (operation is not null) return; using var current = new CancellationTokenSource(); operation = current; foreach (var c in gated) c.IsEnabled = false;
        try { await action(); }
        catch (OperationCanceledException) { if (!closed) status.Text = "Canceled. A selected export destination may contain an incomplete file."; }
        catch (Exception ex) when (AppExceptions.IsOperational(ex)) { AppLog.Error(ex, "Portfolio"); if (!closed) status.Text = ex.Message; }
        finally { operation = null; if (!closed) foreach (var c in gated) c.IsEnabled = true; }
    }
    async Task RefreshAsync(bool single)
    {
        var queries = Queries(single); var progress = new Progress<string>(s => { if (!closed) status.Text = s; });
        await Task.Run(() => session.RefreshAsync(queries, progress, operation!.Token)); if (!closed) { page = 0; Render(); }
    }
    void Render()
    {
        visible = session.Filter(keyword.Text ?? "", service.Text ?? "", changes.IsChecked == true); page = Math.Clamp(page, 0, Math.Max(0, (visible.Count - 1) / 50)); results.Children.Clear();
        foreach (var r in visible.Skip(page * 50).Take(50))
        {
            var check = new CheckBox { Content = Text(r.Display), IsChecked = session.Selected.Contains(r.Id) };
            check.IsCheckedChanged += (_, _) => { if (check.IsChecked == true) session.Selected.Add(r.Id); else session.Selected.Remove(r.Id); UpdateCounts(); }; results.Children.Add(check);
            if (Uri.TryCreate(r.Filing?.Pdf, UriKind.Absolute, out var uri) && uri.Scheme == "https")
            {
                var link = new Button { Content = "Open application PDF" }; link.Click += async (_, _) => await Run(() => { System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(uri.AbsoluteUri) { UseShellExecute = true }); return Task.CompletedTask; }); results.Children.Add(link);
            }
        }
        UpdateCounts();
    }
    void UpdateCounts() => counts.Text = $"{session.Selected.Count} selected · {visible.Count}/{session.Rows.Count} visible · page {page + 1}/{Math.Max(1, (visible.Count + 49) / 50)}";
    async Task<bool> Confirm(string message)
    {
        var dialog = new Window { Title = "Confirm", Width = 560, Height = 300, WindowStartupLocation = WindowStartupLocation.CenterOwner };
        var content = new StackPanel { Margin = new(18), Spacing = 10 }; content.Children.Add(Text(message)); var yes = new Button { Content = "Continue" }; var no = new Button { Content = "Cancel" }; content.Children.Add(Row(yes, no)); dialog.Content = content;
        yes.Click += (_, _) => dialog.Close(true); no.Click += (_, _) => dialog.Close(false); return await dialog.ShowDialog<bool>(this);
    }
    async Task ShowText(string message)
    {
        var dialog = new Window { Title = "District history / summary", Width = 760, Height = 600, WindowStartupLocation = WindowStartupLocation.CenterOwner }; dialog.Content = new ScrollViewer { Content = Text(message), Margin = new(18) }; await dialog.ShowDialog(this);
    }
    async Task ImportAsync()
    {
        var id = RequireList(); var files = await StorageProvider.OpenFilePickerAsync(new() { Title = "Import BEN CSV", AllowMultiple = false, FileTypeFilter = new[] { new FilePickerFileType("CSV") { Patterns = new[] { "*.csv" } } } }); if (files.Count == 0) return;
        using var file = files[0]; await using var stream = await file.OpenReadAsync();
        var preview = await PortfolioSession.ReadCsvAsync(stream, operation!.Token); operation.Token.ThrowIfCancellationRequested();
        if (!await Confirm($"Import {preview.Entries.Count} valid BENs?\n{string.Join("\n", preview.Errors.Take(10))}\nExisting BENs will be skipped. Extra columns are ignored.")) return;
        operation!.Token.ThrowIfCancellationRequested();
        var added = await Task.Run(() => session.Store.ImportEntries(id, preview.Entries, operation.Token));
        if (!closed) { ReloadEntries(); status.Text = $"Imported {added} BENs."; }
    }
    async Task ExportAsync()
    {
        var prepared = session.CreateExport(directory); using var file = await StorageProvider.SaveFilePickerAsync(new() { Title = "Export selected results", SuggestedFileName = "E-rate-portfolio.xlsx", FileTypeChoices = new[] { new FilePickerFileType("Excel") { Patterns = new[] { "*.xlsx" }, MimeTypes = new[] { AndroidBehavior.ExcelContentType } }, new FilePickerFileType("CSV") { Patterns = new[] { "*.csv" }, MimeTypes = new[] { AndroidBehavior.CsvContentType } }, new FilePickerFileType("JSON") { Patterns = new[] { "*.json" }, MimeTypes = new[] { AndroidBehavior.JsonContentType } } } }); if (file is null) return;
        var path = file.TryGetLocalPath() ?? throw new InvalidOperationException("Choose a local destination.");
        await Task.Run(() => prepared.Write(path, operation!.Token)); status.Text = $"Exported {prepared.Table.Count - 1} selected results.";
    }
}
