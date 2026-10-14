using System.Diagnostics;

namespace ERateLookup.Core;

public static class AppExceptions
{
    public static bool IsOperational(Exception exception) =>
        exception is HttpRequestException
            or System.Text.Json.JsonException
            or InvalidOperationException
            or ArgumentException
            or IOException
            or UnauthorizedAccessException
            or FormatException
            or Microsoft.Data.Sqlite.SqliteException
            or System.ComponentModel.Win32Exception;
}

public static class AppLog
{
    public static Action<string>? Sink
    {
        get; set;
    }

    public static void Error(Exception exception, string operation)
    {
        var line = operation + ": " + exception.GetType().Name + ": " + exception.Message;
        Trace.TraceError(line);
        var sink = Sink;
        if (sink is null)
            return;
        try
        {
            sink(line);
        }
        catch (Exception logFailure)
        {
            Trace.TraceError("AppLog sink failed: " + logFailure.Message);
        }
    }
}
