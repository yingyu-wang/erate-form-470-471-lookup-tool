namespace ERate470Lookup.AndroidApp;

// No Android dependencies: the same behavior is exercised by host regression checks.
public static class AndroidBehavior
{
    public const int PageSize = 50;
    public static int PageCount(int count) => Math.Max(1, (count + PageSize - 1) / PageSize);
    public static IEnumerable<T> Page<T>(IReadOnlyList<T> items, int page) =>
        items.Skip(Math.Clamp(page, 0, PageCount(items.Count) - 1) * PageSize).Take(PageSize);

    public static async Task<(bool Available, T? Value)> OptionalAsync<T>(
        Func<CancellationToken, Task<T>> request, CancellationToken caller,
        TimeSpan? timeout = null)
    {
        caller.ThrowIfCancellationRequested();
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(caller);
        deadline.CancelAfter(timeout ?? TimeSpan.FromSeconds(20));
        try
        {
            var value = await request(deadline.Token);
            caller.ThrowIfCancellationRequested();
            return (true, value);
        }
        catch (Exception)
        {
            caller.ThrowIfCancellationRequested();
            // Auxiliary HTTP, timeout, parsing, and cache failures preserve primary data.
            return (false, default);
        }
    }

    public static async Task<bool> RefreshIfStaleAsync(ERate470Lookup.Core.CaliforniaDirectory directory, CancellationToken caller)
    {
        if (!directory.NeedsRefresh) return true;
        return (await OptionalAsync(async token => { await directory.RefreshAsync(token); return true; }, caller)).Available;
    }

    public static async Task CopyExportAsync(Stream source, Stream target, CancellationToken caller)
    {
        caller.ThrowIfCancellationRequested();
        await source.CopyToAsync(target, caller);
        await target.FlushAsync(caller);
        caller.ThrowIfCancellationRequested();
    }
}
