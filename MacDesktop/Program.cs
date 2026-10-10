using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml.Styling;
using Avalonia.Themes.Fluent;
namespace ERate470Lookup.MacDesktop;
internal static class Program
{
    [STAThread] public static void Main(string[] args)=>AppBuilder.Configure<App>().UsePlatformDetect().LogToTrace().StartWithClassicDesktopLifetime(args);
}
public sealed class App:Application
{
    public override void Initialize()
    {
        Styles.Add(new FluentTheme());
        Styles.Add(new StyleInclude(new Uri("avares://ERateLookup/")){Source=new Uri("avares://Avalonia.Controls.DataGrid/Themes/Fluent.xaml")});
    }
    public override void OnFrameworkInitializationCompleted()
    {
        if(ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)desktop.MainWindow=new MainWindow();
        base.OnFrameworkInitializationCompleted();
    }
}
