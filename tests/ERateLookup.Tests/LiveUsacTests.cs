using ERateLookup.Core;
using Xunit;

namespace ERateLookup.Tests;

public sealed class LiveUsacFactAttribute : FactAttribute
{
    public LiveUsacFactAttribute()
    {
        if (!string.Equals(Environment.GetEnvironmentVariable("ERATE_LIVE_USAC"), "1", StringComparison.Ordinal))
            Skip = "Live USAC checks run from the nightly workflow (ERATE_LIVE_USAC=1).";
    }
}

public class LiveUsacTests
{
    [LiveUsacFact]
    [Trait("Category", "Live")]
    public async Task PublishedFilingsForKnownCaliforniaBens()
    {
        var directory = Path.Combine(Path.GetTempPath(), "ERateLive-" + Guid.NewGuid());
        try
        {
            using var liveHttp = new HttpClient { Timeout = TimeSpan.FromSeconds(30) };
            using var live = new UsacClient(liveHttp, Path.Combine(directory, "live"));
            var data = await live.GetFilingsAsync("133193", 2027, true, default);
            Assert.True(data.Data.Count > 0, "Live FY2027 query");
            var older471 = await live.GetFilingsAsync("106359", 2026, FilingForm.Form471, true, default);
            Assert.True(older471.Data.Count > 0, "Live California FY2026 Form 471 mapping");
            Assert.All(older471.Data, filing => Assert.Equal("106359", filing.Ben));
            var future471 = await live.GetFilingsAsync("106359", 2027, FilingForm.Form471, true, default);
            Assert.NotNull(future471.Data);
        }
        finally
        {
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
            if (Directory.Exists(directory))
                Directory.Delete(directory, true);
        }
    }
}
