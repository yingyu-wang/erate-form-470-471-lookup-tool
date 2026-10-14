using ERateLookup.Core;

namespace ERateLookup.Desktop;

/// <summary>
/// Adds the BEN identified in the main window to a watchlist without retyping it.
/// </summary>
public sealed class AddToWatchlistDialog : Form
{
    public string ChosenListName { get; private set; } = "";
    public int AddedCount { get; private set; }
    public int SkippedCount { get; private set; }
    readonly PortfolioStore store;
    readonly List<WatchlistEntry> entries;
    readonly bool skipExisting;
    readonly ComboBox lists = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 280 };
    readonly TextBox name = new() { Width = 280, PlaceholderText = "Display name (optional)" };
    readonly TextBox tags = new() { Width = 280, PlaceholderText = "Tags (optional)" };

    public AddToWatchlistDialog(PortfolioStore store, string ben, string suggestedName)
        : this(store, [new(ben, suggestedName, "")], false) { }

    public AddToWatchlistDialog(PortfolioStore store, IReadOnlyList<WatchlistEntry> entries, bool skipExisting = true)
    {
        this.store = store;
        this.entries = entries.DistinctBy(e => e.Ben).ToList();
        if (this.entries.Count == 0) throw new ArgumentException("Select at least one BEN.");
        this.skipExisting = skipExisting;
        Text = this.entries.Count == 1 ? $"Add BEN {this.entries[0].Ben} to watchlist" : $"Add {this.entries.Count} BENs to watchlist";
        Width = this.entries.Count == 1 ? 340 : 540;
        Height = this.entries.Count == 1 ? 300 : 430;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        StartPosition = FormStartPosition.CenterParent;
        MaximizeBox = false;
        MinimizeBox = false;
        var layout = new TableLayoutPanel { Dock = DockStyle.Fill, Padding = new(16), ColumnCount = 1, RowCount = 7 };
        Controls.Add(layout);
        lists.DataSource = store.Lists();
        name.Text = this.entries[0].Name;
        layout.Controls.Add(new Label { Text = "Watchlist", AutoSize = true }, 0, 0);
        layout.Controls.Add(lists, 0, 1);
        layout.Controls.Add(new Label { Text = this.entries.Count == 1 ? "Display name" : "Selected districts · existing BENs will be kept", AutoSize = true }, 0, 2);
        if (this.entries.Count == 1) layout.Controls.Add(name, 0, 3);
        else layout.Controls.Add(new ListBox { DataSource = this.entries, Dock = DockStyle.Fill, Height = 150 }, 0, 3);
        layout.Controls.Add(new Label { Text = "Tags", AutoSize = true }, 0, 4);
        layout.Controls.Add(tags, 0, 5);
        var buttons = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.RightToLeft, AutoSize = true };
        var ok = new Button { Text = "Save", AutoSize = true };
        var cancel = new Button { Text = "Cancel", AutoSize = true, DialogResult = DialogResult.Cancel };
        ok.Click += (_, _) => Save();
        buttons.Controls.Add(ok);
        buttons.Controls.Add(cancel);
        layout.Controls.Add(buttons, 0, 6);
        AcceptButton = ok;
        CancelButton = cancel;
    }

    void Save()
    {
        try
        {
            if (lists.SelectedItem is not Watchlist list) throw new InvalidOperationException("No watchlist available.");
            var pending = entries.Select(e => e with { Name = entries.Count == 1 ? name.Text : e.Name, Tags = tags.Text }).ToList();
            if (skipExisting) AddedCount = store.ImportEntries(list.Id, pending);
            else { store.SaveEntry(list.Id, pending[0]); AddedCount = 1; }
            SkippedCount = pending.Count - AddedCount;
            ChosenListName = list.Name;
            DialogResult = DialogResult.OK;
            Close();
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException or Microsoft.Data.Sqlite.SqliteException)
        {
            AppLog.Error(ex, "Add BEN to watchlist");
            MessageBox.Show(this, ex.Message, "Add BEN to watchlist");
        }
    }
}
