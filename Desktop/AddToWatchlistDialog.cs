using ERate470Lookup.Core;

namespace ERate470Lookup.Desktop;

/// <summary>
/// Adds the BEN identified in the main window to a watchlist without retyping it.
/// </summary>
public sealed class AddToWatchlistDialog : Form
{
    public string ChosenListName { get; private set; } = "";
    readonly PortfolioStore store;
    readonly string ben;
    readonly ComboBox lists = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 280 };
    readonly TextBox name = new() { Width = 280, PlaceholderText = "Display name (optional)" };
    readonly TextBox tags = new() { Width = 280, PlaceholderText = "Tags (optional)" };

    public AddToWatchlistDialog(PortfolioStore store, string ben, string suggestedName)
    {
        this.store = store;
        this.ben = ben;
        Text = $"Add BEN {ben} to watchlist";
        Width = 340;
        Height = 300;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        StartPosition = FormStartPosition.CenterParent;
        MaximizeBox = false;
        MinimizeBox = false;
        var layout = new TableLayoutPanel { Dock = DockStyle.Fill, Padding = new(16), ColumnCount = 1, RowCount = 7 };
        Controls.Add(layout);
        lists.DataSource = store.Lists();
        name.Text = suggestedName;
        layout.Controls.Add(new Label { Text = "Watchlist", AutoSize = true }, 0, 0);
        layout.Controls.Add(lists, 0, 1);
        layout.Controls.Add(new Label { Text = "Display name", AutoSize = true }, 0, 2);
        layout.Controls.Add(name, 0, 3);
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
            store.SaveEntry(list.Id, new WatchlistEntry(ben, name.Text, tags.Text));
            ChosenListName = list.Name;
            DialogResult = DialogResult.OK;
            Close();
        }
        catch (Exception ex)
        {
            // Keep the dialog open so the user can fix the entry and retry.
            MessageBox.Show(this, ex.Message, "Add BEN to watchlist");
        }
    }
}
