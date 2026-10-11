using System.Net;
using ERate470Lookup.AndroidApp;
using ERate470Lookup.Core;

static class AndroidBehaviorChecks
{
    static void Check(bool passed, string name)
    {
        if (!passed) throw new Exception("FAIL: " + name);
        Console.WriteLine("PASS: " + name);
    }

    public static async Task RunAsync()
    {
        foreach (var error in new Exception[] { new HttpRequestException(), new TaskCanceledException(), new System.Text.Json.JsonException(), new IOException() })
        {
            var result = await AndroidBehavior.OptionalAsync<int>(_ => Task.FromException<int>(error), default);
            Check(!result.Available, "Optional " + error.GetType().Name + " preserves primary results");
        }
        var success = await AndroidBehavior.OptionalAsync(_ => Task.FromResult(false), default);
        Check(success.Available && !success.Value, "Optional false result stays distinct from unavailable");
        var timeout = await AndroidBehavior.OptionalAsync(async ct => { await Task.Delay(Timeout.Infinite, ct); return true; }, default, TimeSpan.FromMilliseconds(10));
        Check(!timeout.Available, "Auxiliary deadline preserves primary results");
        using var canceled = new CancellationTokenSource();
        var propagated = false;
        try { await AndroidBehavior.OptionalAsync<int>(_ => { canceled.Cancel(); return Task.FromException<int>(new IOException()); }, canceled.Token); }
        catch (OperationCanceledException) { propagated = true; }
        Check(propagated, "User cancellation propagates through optional checks");

        var data = Enumerable.Range(0, 5001).ToArray();
        var pages = Enumerable.Range(0, AndroidBehavior.PageCount(data.Length)).Select(p => AndroidBehavior.Page(data, p).ToArray()).ToArray();
        Check(pages.All(p => p.Length <= 50) && pages.SelectMany(p => p).SequenceEqual(data), "Large result paging bounds views without losing applications");
        Check(AndroidBehavior.Page(data, int.MaxValue).SequenceEqual(new[] { 5000 }), "Page bounds clamp safely");
        Check(AndroidBehavior.Page(Array.Empty<int>(), 0).Count() == 0, "Empty lookup has a safe page");

        var folder = Path.Combine(Path.GetTempPath(), "AndroidChecks-" + Guid.NewGuid());
        try
        {
            using var handler = new OfflineHandler(); using var http = new HttpClient(handler);
            var directory = new CaliforniaDirectory(http, folder);
            var entities = new List<DirectoryRow> { new() { Number = "123", Name = "Saved school", State = "CA", Type = "School District" } };
            directory.Replace(entities, [], DateTimeOffset.UtcNow);
            Check(await AndroidBehavior.RefreshIfStaleAsync(directory, default) && handler.Count == 0, "Fresh startup snapshot skips network refresh");
            directory.Replace(entities, [], DateTimeOffset.UtcNow.AddDays(-2));
            Check(!await AndroidBehavior.RefreshIfStaleAsync(directory, default) && handler.Count == 1 && directory.Search("Saved").Count == 1, "Stale startup attempts refresh and preserves offline data on failure");

            using var generation = new CancellationTokenSource();
            IEnumerable<string[]> Rows()
            {
                yield return ["header"];
                generation.Cancel();
                yield return ["must not be written"];
            }
            var csv = Path.Combine(folder, "canceled.csv"); propagated = false;
            try { DataExporter.WriteCsv(csv, Rows(), generation.Token); }
            catch (OperationCanceledException) { propagated = true; }
            Check(propagated && !File.ReadAllText(csv).Contains("must not"), "CSV generation stops at canceled row");
            var excel = Path.Combine(folder, "canceled.xlsx"); propagated = false;
            try { DataExporter.WriteExcel(excel, [["header"]], generation.Token); }
            catch (OperationCanceledException) { propagated = true; }
            Check(propagated && !File.Exists(excel), "Canceled Excel generation never starts output");

            using var copying = new CancellationTokenSource();
            using var source = new CancelOnReadStream(copying); using var target = new MemoryStream(); propagated = false;
            try { await AndroidBehavior.CopyExportAsync(source, target, copying.Token); }
            catch (OperationCanceledException) { propagated = true; }
            Check(propagated && target.Length == 0, "Export copy honors active cancellation");
        }
        finally { if (Directory.Exists(folder)) Directory.Delete(folder, true); }
    }

    sealed class OfflineHandler : HttpMessageHandler
    {
        public int Count;
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            Count++;
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.ServiceUnavailable));
        }
    }

    sealed class CancelOnReadStream(CancellationTokenSource cancellation) : Stream
    {
        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => 1024;
        public override long Position { get => 0; set => throw new NotSupportedException(); }
        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken ct = default)
        {
            cancellation.Cancel();
            return ValueTask.FromCanceled<int>(ct);
        }
        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        public override void Flush() => throw new NotSupportedException();
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }
}
