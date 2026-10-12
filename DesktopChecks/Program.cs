using System.Net;
using System.Reflection;
using ERate470Lookup.Core;
using ERate470Lookup.Desktop;
using Microsoft.Data.Sqlite;

internal static class Program
{
    const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
    static T Field<T>(object target, string name) => (T)target.GetType().GetField(name, Private)!.GetValue(target)!;
    static Task InvokeAsync(object target, string name, params object[] args) =>
        (Task)target.GetType().GetMethod(name, Private)!.Invoke(target, args)!;
    static void Check(bool ok, string message)
    { if (!ok) throw new Exception("FAIL: " + message); Console.WriteLine("PASS: " + message); }

    [STAThread]
    static int Main()
    {
        ApplicationConfiguration.Initialize();
        var root = Path.Combine(Path.GetTempPath(), "ERateDesktopChecks-" + Guid.NewGuid());
        try
        {
            foreach (var problem in new[] { "future", "corrupt", "unopenable" })
            {
                var folder = Path.Combine(root, problem); Directory.CreateDirectory(folder);
                var path = Path.Combine(folder, "portfolio.sqlite3");
                if (problem == "future")
                {
                    using var db = new SqliteConnection($"Data Source={path};Pooling=False"); db.Open();
                    using var cmd = db.CreateCommand(); cmd.CommandText = "PRAGMA user_version=99"; cmd.ExecuteNonQuery();
                }
                else if (problem == "corrupt") File.WriteAllText(path, "not a database");
                else Directory.CreateDirectory(path);
                var before = File.Exists(path) ? File.ReadAllBytes(path) : null;
                using var window = new MainForm(folder);
                Check(Field<Button>(window, "search").Enabled, $"Lookup opens with {problem} portfolio data");
                Check(before is null ? Directory.Exists(path) : before.SequenceEqual(File.ReadAllBytes(path)),
                    $"Opening lookup leaves {problem} portfolio untouched");
            }

            var data = Path.Combine(root, "normal");
            PrepareAsync(data).GetAwaiter().GetResult();
            var store = new PortfolioStore(data); var list = store.CreateList("Test list");
            using var form = new MainForm(data);
            Exception? failure = null;
            form.Shown += async (_, _) =>
            {
                try
                {
                    Field<ComboBox>(form, "year").SelectedItem = 2025;
                    Field<TextBox>(form, "customer").Text = "First";
                    await InvokeAsync(form, "SearchAsync", false);
                    Field<ListBox>(form, "matches").SelectedIndex = 0;
                    await InvokeAsync(form, "ChooseAsync");
                    Check(Field<string>(form, "selectedName").StartsWith("First District"), "Partial-name selection retains its district name");
                    SaveFromMain(form, list.Id, "First District [School District]");
                    Check(store.Entries(list.Id).Single().Ben == "00123", "Main-window action saves the matched BEN to the chosen watchlist");

                    Field<TextBox>(form, "customer").Text = "00456";
                    Check(!Field<Button>(form, "saveBen").Enabled, "Editing the customer immediately disables saving the previous BEN");
                    await InvokeAsync(form, "SearchAsync", false);
                    SaveFromMain(form, list.Id, "Second District");
                    Check(store.Entries(list.Id).Single(e => e.Ben == "00456").Name == "Second District",
                        "Direct BEN uses its own budget name instead of the previous district name");

                    Field<TextBox>(form, "customer").Text = "00789";
                    await InvokeAsync(form, "SearchAsync", false);
                    SaveFromMain(form, list.Id, "");
                    Check(store.Entries(list.Id).Single(e => e.Ben == "00789").Name == "", "Unknown direct BEN keeps its name blank");
                }
                catch (Exception ex) { failure = ex; }
                finally { form.Close(); }
            };
            Application.Run(form);
            if (failure is not null) throw failure;
            Console.WriteLine("All Windows UI regression checks passed."); return 0;
        }
        catch (Exception ex) { Console.Error.WriteLine(ex); return 1; }
        finally { SqliteConnection.ClearAllPools(); if (Directory.Exists(root)) Directory.Delete(root, true); }
    }

    static void SaveFromMain(MainForm owner, string listId, string expectedName)
    {
        Exception? failure = null; var visited = false;
        using var timer = new System.Windows.Forms.Timer { Interval = 100 };
        timer.Tick += (_, _) =>
        {
            var dialog = Application.OpenForms.OfType<AddToWatchlistDialog>().SingleOrDefault();
            if (dialog is null) return;
            timer.Stop(); visited = true;
            try
            {
                Check(Field<TextBox>(dialog, "name").Text == expectedName, "Add dialog suggests the current BEN's name");
                var picker = Field<ComboBox>(dialog, "lists");
                picker.SelectedItem = picker.Items.Cast<Watchlist>().Single(l => l.Id == listId);
                Field<TextBox>(dialog, "tags").Text = "Reviewed";
                dialog.AcceptButton!.PerformClick();
            }
            catch (Exception ex) { failure = ex; dialog.Close(); }
        };
        timer.Start(); Field<Button>(owner, "saveBen").PerformClick();
        if (failure is not null) throw failure;
        Check(visited, "Main-window action opens the add dialog");
    }

    static async Task PrepareAsync(string folder)
    {
        using var http = new HttpClient(new EmptyFilings()); var cache = Path.Combine(folder, "cache");
        var directory = new CaliforniaDirectory(http, cache);
        directory.Replace([new() { Number = "00123", Name = "First District", State = "CA", City = "Test", Type = "School District" }],
            [new() { Ben = "00456", Name = "Second District", Cycle = "FY2021-2025" }], DateTimeOffset.UtcNow);
        var client = new UsacClient(http, cache);
        foreach (var ben in new[] { "00123", "00456", "00789" })
            await client.GetFilingsAsync(ben, 2025, FilingForm.Form470, false, default);
    }
    sealed class EmptyFilings : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("[]") });
    }
}
