using ERateLookup.Desktop;
using Microsoft.Data.Sqlite;
using Xunit;

namespace ERateLookup.Desktop.Tests;

public class CacheDisposalTests
{
    [Fact]
    public void DisposingMainFormReleasesTheCacheFolder()
    {
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            try { Run(); }
            catch (Exception ex) { failure = ex; }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        Assert.True(thread.Join(TimeSpan.FromMinutes(1)), "Cache disposal test timed out.");
        if (failure is not null) throw new Exception(failure.ToString(), failure);
    }

    static void Run()
    {
        ApplicationConfiguration.Initialize();
        var folder = Path.Combine(Path.GetTempPath(), "ERateCacheDispose-" + Guid.NewGuid());
        MainForm? form = null;
        try
        {
            form = new MainForm(folder);
            var cacheFile = Path.Combine(folder, "cache", "cache.sqlite3");
            Assert.True(File.Exists(cacheFile));
            using (var db = new SqliteConnection("Data Source=" + cacheFile + ";Pooling=False"))
            {
                db.Open();
                using var write = db.CreateCommand();
                write.CommandText = "INSERT INTO query_cache(query_key, fetched_utc, payload_json) VALUES('dispose-check', '2026-10-14T00:00:00.0000000+00:00', '[]')";
                Assert.Equal(1, write.ExecuteNonQuery());
                using var read = db.CreateCommand();
                read.CommandText = "SELECT payload_json FROM query_cache WHERE query_key='dispose-check'";
                Assert.Equal("[]", read.ExecuteScalar());
            }

            // Closing the form is what releases UsacClient's long-lived connection.
            // Removing this Dispose leaves cache.sqlite3 locked, and Directory.Delete fails on Windows the way run #55 did.
            form.Dispose();
            form = null;
            SqliteConnection.ClearAllPools();
            Directory.Delete(folder, true);
            Assert.False(Directory.Exists(folder));
        }
        finally
        {
            form?.Dispose();
            SqliteConnection.ClearAllPools();
        }
    }
}
