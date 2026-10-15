using ERateLookup.Core;

namespace ERateLookup.Cli;

internal static class Program
{
    static async Task<int> Main(string[] args)
    {
        var parsed = CliCommand.Parse(args);
        if (parsed.Help)
        {
            Console.Out.WriteLine(CliCommand.HelpText);
            return CliCommand.ExitFound;
        }

        if (parsed.Error is not null || parsed.Options is null)
        {
            Console.Error.WriteLine(parsed.Error ?? "Invalid arguments.");
            Console.Error.WriteLine(CliCommand.HelpText);
            return CliCommand.ExitBadArgs;
        }

        var cache = Path.Combine(Path.GetTempPath(), "ERateLookup-cli");
        using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(60) };
        using var client = new UsacClient(http, cache);
        return await CliCommand.RunAsync(parsed.Options, client, Console.Out, Console.Error, CancellationToken.None).ConfigureAwait(false);
    }
}
