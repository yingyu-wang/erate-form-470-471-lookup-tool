using Foundation;
using UIKit;
namespace ERate470Lookup.iOS;
public static class Program
{
    public static void Main(string[] args) => UIApplication.Main(args, null, typeof(AppDelegate));
}
[Register("AppDelegate")]
public sealed class AppDelegate : UIApplicationDelegate
{
    public override UIWindow? Window { get; set; }
    public override bool FinishedLaunching(UIApplication application, NSDictionary? launchOptions)
    {
        Window = new UIWindow(UIScreen.MainScreen.Bounds);
        Window.RootViewController = new UINavigationController(new LookupController());
        Window.TintColor = UIColor.FromRGB(0, 117, 104);
        Window.MakeKeyAndVisible();
        return true;
    }
}
