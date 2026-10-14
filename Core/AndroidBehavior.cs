namespace ERateLookup.Core;

public static class AndroidBehavior
{
    public const int PageSize = 50;

    public static int PageCount(int count) => Math.Max(1, (count + PageSize - 1) / PageSize);

    public static IEnumerable<T> Page<T>(IReadOnlyList<T> items, int page) =>
        items.Skip(Math.Clamp(page, 0, PageCount(items.Count) - 1) * PageSize).Take(PageSize);

    public static async Task<(bool Available, T? Value)> OptionalAsync<T>(
        Func<CancellationToken, Task<T>> request,
        CancellationToken caller,
        TimeSpan? timeout = null)
    {
        caller.ThrowIfCancellationRequested();
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(caller);
        deadline.CancelAfter(timeout ?? TimeSpan.FromSeconds(20));
        try
        {
            var value = await request(deadline.Token).ConfigureAwait(false);
            caller.ThrowIfCancellationRequested();
            return (true, value);
        }
        catch (Exception ex) when (ex is HttpRequestException
            or OperationCanceledException
            or System.Text.Json.JsonException
            or IOException
            or InvalidOperationException
            or UnauthorizedAccessException
            or Microsoft.Data.Sqlite.SqliteException)
        {
            if (caller.IsCancellationRequested)
                caller.ThrowIfCancellationRequested();
            AppLog.Error(ex, "Optional request");
            return (false, default);
        }
    }

    public static async Task<bool> RefreshIfStaleAsync(CaliforniaDirectory directory, CancellationToken caller)
    {
        if (!directory.NeedsRefresh)
            return true;
        return (await OptionalAsync(async token =>
        {
            await directory.RefreshAsync(token).ConfigureAwait(false);
            return true;
        }, caller).ConfigureAwait(false)).Available;
    }

    public static async Task CopyExportAsync(Stream source, Stream target, CancellationToken caller)
    {
        caller.ThrowIfCancellationRequested();
        await source.CopyToAsync(target, caller).ConfigureAwait(false);
        await target.FlushAsync(caller).ConfigureAwait(false);
        caller.ThrowIfCancellationRequested();
    }
}
