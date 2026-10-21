using System.Net;
using System.Reflection;
using ERateLookup.Core;
using ERateLookup.Desktop;
using Xunit;
using Microsoft.Data.Sqlite;

namespace ERateLookup.Desktop.Tests;

public class MainFormWatchlistTests
{
    const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
    static T Field<T>(object target, string name) => (T)target.GetType().GetField(name, Private)!.GetValue(target)!;
    static Task InvokeAsync(object target, string name, params object[] args) =>
        (Task)target.GetType().GetMethod(name, Private)!.Invoke(target, args)!;
    static void Check(bool ok, string message)
    { if (!ok) throw new Exception("FAIL: " + message); Console.WriteLine("PASS: " + message); }

    [Fact]
    public void SearchResultWatchlistRegression()
    {
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            try { Run(); }
            catch (Exception ex) { failure = ex; }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        Assert.True(thread.Join(TimeSpan.FromMinutes(2)), "Windows UI regression timed out.");
        if (failure is not null) throw new Exception(failure.ToString(), failure);
    }

    static void Run()
    {
        var workspace = Environment.GetEnvironmentVariable("GITHUB_WORKSPACE");
        if (!string.IsNullOrWhiteSpace(workspace))
            Directory.SetCurrentDirectory(workspace);
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
                window.Close();
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

                    Field<ComboBox>(form, "year").SelectedItem = 2027;
                    Field<TextBox>(form, "customer").Text = "No filings";
                    await InvokeAsync(form, "SearchAsync", false);
                    var matches = Field<ListBox>(form, "matches");
                    Check(matches.Items.Count == 2, "Partial search finds both districts without 2027 filings or budgets");
                    matches.ClearSelected(); matches.SetSelected(0, true); matches.SetSelected(1, true);
                    Capture(form, "search-results-smoke.png");
                    Check(Field<Button>(form, "saveMatches").Enabled && !Field<Button>(form, "choose").Enabled,
                        "Multiple search results enable saving and require one district for a filing lookup");
                    Check(!Field<bool>(form, "lookupComplete") && Field<string>(form, "selectedBen") == "",
                        "Search-result save is available before any filing lookup");
                    var searchList = store.CreateList("Search results");
                    SaveFromMain(form, searchList.Id, null, "saveMatches", ["01234", "05678"]);
                    var saved = store.Entries(searchList.Id);
                    Check(saved.Count == 2 && saved.Single(e => e.Ben == "01234").Name == "No filings District A [School District]"
                        && saved.Single(e => e.Ben == "05678").Name == "No filings District B [School District]" && saved.All(e => e.Tags == "Reviewed"),
                        "Selected districts save together with their own names and leading-zero BENs");
                    store.SaveEntry(searchList.Id, new("01234", "Owner supplied name", "Keep"), true);
                    SaveFromMain(form, searchList.Id, null, "saveMatches", ["01234", "05678"]);
                    Check(store.Entries(searchList.Id).Count == 2 && store.Entries(searchList.Id).Single(e => e.Ben == "01234") is { Name: "Owner supplied name", Tags: "Keep" },
                        "Repeated search-result save skips existing BENs without overwriting them");

                    var canceled = store.CreateList("Canceled save");
                    SaveFromMain(form, canceled.Id, null, "saveMatches", ["01234", "05678"], cancel: true);
                    Check(store.Entries(canceled.Id).Count == 0, "Canceling a multi-result save adds no BENs");
                    matches.ClearSelected(); matches.SetSelected(0, true);
                    var single = store.CreateList("One search result");
                    SaveFromMain(form, single.Id, "No filings District A [School District]", "saveMatches", ["01234"]);
                    Check(store.Entries(single.Id).Single().Ben == "01234", "One search result also saves before a filing lookup");

                    await InvokeAsync(form, "ChooseAsync");
                    Check(Field<bool>(form, "lookupComplete") && Field<List<Filing>>(form, "results").Count == 0,
                        "FY2027 Form 470 can complete with no published applications");
                    var empty = store.CreateList("Empty 2027 filings");
                    SaveFromMain(form, empty.Id, "No filings District A [School District]");
                    Check(store.Entries(empty.Id).Single().Ben == "01234", "BEN remains savable after an empty FY2027 Form 470 lookup");
                    Field<TextBox>(form, "customer").Text = "Different search";
                    Check(!Field<Button>(form, "saveMatches").Enabled && matches.SelectedItems.Count == 0,
                        "Changing the search clears candidate selections and disables their save action");
                }
                catch (Exception ex) { failure = ex; }
                finally { form.Close(); }
            };
            Application.Run(form);
            if (failure is not null) throw failure;
            Console.WriteLine("All Windows UI regression checks passed.");
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            GC.Collect();
            GC.WaitForPendingFinalizers();
            SqliteConnection.ClearAllPools();
            if (Directory.Exists(root)) Directory.Delete(root, true);
        }
    }

    static void SaveFromMain(MainForm owner, string listId, string? expectedName, string action = "saveBen", string[]? expectedBens = null, bool cancel = false)
    {
        Exception? failure = null; var visited = false;
        var beforeComplete = Field<bool>(owner, "lookupComplete");
        var beforeBen = Field<string>(owner, "selectedBen");
        var beforeFilings = Field<List<Filing>>(owner, "results").ToArray();
        using var timer = new System.Windows.Forms.Timer { Interval = 100 };
        timer.Tick += (_, _) =>
        {
            var dialog = Application.OpenForms.OfType<AddToWatchlistDialog>().SingleOrDefault();
            if (dialog is null) return;
            timer.Stop(); visited = true;
            try
            {
                if (expectedName is not null) Check(Field<TextBox>(dialog, "name").Text == expectedName, "Add dialog suggests the current BEN's name");
                if (expectedBens is not null) Check(Field<List<WatchlistEntry>>(dialog, "entries").Select(e => e.Ben).Order().SequenceEqual(expectedBens.Order()),
                    "Save dialog contains exactly the selected search-result BENs");
                var picker = Field<ComboBox>(dialog, "lists");
                picker.SelectedItem = picker.Items.Cast<Watchlist>().Single(l => l.Id == listId);
                Field<TextBox>(dialog, "tags").Text = "Reviewed";
                if (expectedBens?.Length > 1) Capture(dialog, "search-save-dialog-smoke.png");
                if (cancel) dialog.CancelButton!.PerformClick(); else dialog.AcceptButton!.PerformClick();
            }
            catch (Exception ex) { failure = ex; dialog.Close(); }
        };
        timer.Start(); Field<Button>(owner, action).PerformClick();
        if (failure is not null) throw failure;
        Check(visited, "Main-window action opens the add dialog");
        if (action == "saveMatches") Check(Field<bool>(owner, "lookupComplete") == beforeComplete
            && Field<string>(owner, "selectedBen") == beforeBen && Field<List<Filing>>(owner, "results").SequenceEqual(beforeFilings),
            "Saving or canceling search matches leaves filing lookup state unchanged");
    }

    static void Capture(Form form, string path)
    {
        form.Refresh();
        using var bitmap = new Bitmap(form.Width, form.Height);
        form.DrawToBitmap(bitmap, new Rectangle(Point.Empty, form.Size));
        bitmap.Save(path, System.Drawing.Imaging.ImageFormat.Png);
    }

    static async Task PrepareAsync(string folder)
    {
        using var http = new HttpClient(new EmptyFilings()); var cache = Path.Combine(folder, "cache");
        var directory = new CaliforniaDirectory(http, cache);
        directory.Replace([new() { Number = "00123", Name = "First District", State = "CA", City = "Test", Type = "School District" },
            new() { Number = "01234", Name = "No filings District A", State = "CA", City = "Test", Type = "School District" },
            new() { Number = "05678", Name = "No filings District B", State = "CA", City = "Test", Type = "School District" }],
            [new() { Ben = "00456", Name = "Second District", State = "CA", Cycle = "FY2021-2025" }], DateTimeOffset.UtcNow);
        Check(directory.GetBudget("00456", 2025)?.Name == "Second District", "Budget-name fallback fixture is present in the California snapshot");
        using var client = new UsacClient(http, cache);
        foreach (var ben in new[] { "00123", "00456", "00789" })
            await client.GetFilingsAsync(ben, 2025, FilingForm.Form470, false, default);
        foreach (var ben in new[] { "01234", "05678" })
        {
            await client.GetFilingsAsync(ben, 2027, FilingForm.Form470, false, default);
            await client.HasNewCycleForm470Async(ben, false, default);
        }
    }
    sealed class EmptyFilings : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            var uri = request.RequestUri!;
            var query = Uri.UnescapeDataString(uri.Query);
            var body = uri.AbsolutePath.Contains("/api/views/", StringComparison.Ordinal)
                ? "{\"rowsUpdatedAt\":1}"
                : query.Contains("count(*)", StringComparison.Ordinal) ? "[{\"count\":\"0\"}]" : "[]";
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(body) });
        }
    }
}
