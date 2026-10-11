using Foundation;
using UIKit;
using ObjCRuntime;
namespace ERate470Lookup.iOS;
public static class Program
{
    public static void Main(string[] args)
    {
        Console.WriteLine("E-rate: managed entry point reached.");
        UIApplication.Main(args, null, typeof(AppDelegate));
    }
}
[Register("AppDelegate")]
public class AppDelegate : UIApplicationDelegate
{
    public AppDelegate() => Console.WriteLine("E-rate: app delegate created.");

    [Export("application:didFinishLaunchingWithOptions:")]
    public override bool FinishedLaunching(UIApplication application, NSDictionary? launchOptions)
    {
        Console.WriteLine("E-rate: finished launching.");
        return true;
    }

    [Export("application:configurationForConnectingSceneSession:options:")]
    public override UISceneConfiguration GetConfiguration(UIApplication application, UISceneSession connectingSceneSession, UISceneConnectionOptions options)
    {
        Console.WriteLine("E-rate: configuring scene.");
        return new UISceneConfiguration("Default Configuration", connectingSceneSession.Role)
        {
            DelegateClass = new Class(typeof(SceneDelegate))
        };
    }
}
[Register("SceneDelegate")]
public class SceneDelegate : UIWindowSceneDelegate
{
    public override UIWindow? Window { get; set; }

    [Export("scene:willConnectToSession:options:")]
    public override void WillConnect(UIScene scene, UISceneSession session, UISceneConnectionOptions connectionOptions)
    {
        Console.WriteLine("E-rate: scene connecting.");
        if (scene is not UIWindowScene windowScene) return;
        Window = new UIWindow(windowScene)
        {
            RootViewController = new UINavigationController(new LookupController()),
            TintColor = UIColor.FromRGB(0, 117, 104)
        };
        Window.MakeKeyAndVisible();
    }
}
