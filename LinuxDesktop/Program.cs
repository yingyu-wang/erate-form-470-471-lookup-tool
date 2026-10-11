using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml.Styling;
using Avalonia.Themes.Fluent;
using Avalonia.Threading;
using ERate470Lookup.MacDesktop;

namespace ERate470Lookup.LinuxDesktop;
internal static class Program
{
    internal static bool SmokeTest;
    [STAThread]
    public static int Main(string[] args)
    {
        SmokeTest = args.Contains("--smoke-test", StringComparer.Ordinal);
        return AppBuilder.Configure<App>().UsePlatformDetect().LogToTrace().StartWithClassicDesktopLifetime(args);
    }
}
public sealed class App : Application
{
    public override void Initialize()
    {
        Styles.Add(new FluentTheme());
        Styles.Add(new StyleInclude(new Uri("avares://ERateLookup/"))
        { Source = new Uri("avares://Avalonia.Controls.DataGrid/Themes/Fluent.xaml") });
    }
    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            var window = new MainWindow(refreshOnOpen: !Program.SmokeTest);
            using var icon = typeof(App).Assembly.GetManifestResourceStream("ERateLookup.png");
            if (icon is not null) window.Icon = new WindowIcon(icon);
            desktop.MainWindow = window;
            if (Program.SmokeTest)
                window.Opened += (_, _) => DispatcherTimer.RunOnce(() =>
                {
                    if (window.Content is null || window.Bounds.Width <= 0 || !window.IsVisible)
                        desktop.Shutdown(1);
                    else
                    {
                        Console.WriteLine("Linux Avalonia window launch passed.");
                        desktop.Shutdown(0);
                    }
                }, TimeSpan.FromSeconds(2));
        }
        base.OnFrameworkInitializationCompleted();
    }
}
