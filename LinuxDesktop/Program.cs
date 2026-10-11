using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml.Styling;
using Avalonia.Themes.Fluent;
using Avalonia.Threading;
using ERate470Lookup.MacDesktop;
using ERate470Lookup.Core;
using Avalonia.Media.Imaging;

namespace ERate470Lookup.LinuxDesktop;
internal static class Program
{
    internal static bool SmokeTest;
    internal static bool PortfolioSmoke;
    [STAThread]
    public static int Main(string[] args)
    {
        PortfolioSmoke=args.Contains("--smoke-portfolio",StringComparer.Ordinal);
        SmokeTest = PortfolioSmoke||args.Contains("--smoke-test", StringComparer.Ordinal);
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
            Window window;
            if(Program.PortfolioSmoke)
            {
                var folder=Path.Combine(Path.GetTempPath(),"ERatePortfolioSmoke-"+Guid.NewGuid().ToString("N"));
                var http=new HttpClient();var directory=new CaliforniaDirectory(http,folder);directory.LoadSeedIfEmpty();
                window=new PortfolioWindow(new UsacClient(http,folder),folder,directory,new FilingWindow(http,folder));
                window.Closed+=(_,_)=>{http.Dispose();Directory.Delete(folder,true);};
            }
            else window = new MainWindow(refreshOnOpen: !Program.SmokeTest);
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
                        if(Program.PortfolioSmoke)
                        {
                            using var bitmap=new RenderTargetBitmap(new PixelSize((int)window.Bounds.Width,(int)window.Bounds.Height));
                            bitmap.Render(window);bitmap.Save("portfolio-smoke.png");
                        }
                        Console.WriteLine("Linux Avalonia window launch passed.");
                        desktop.Shutdown(0);
                    }
                }, TimeSpan.FromSeconds(2));
        }
        base.OnFrameworkInitializationCompleted();
    }
}
