using Avalonia;
using OpenpilotSdk.Runtime;

namespace OpenpilotToolkit.Avalonia;

internal static class Program
{
    [STAThread]
    public static void Main(string[] args)
    {
        OpenpilotHost.Configure();
        BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
    }

    public static AppBuilder BuildAvaloniaApp()
    {
        return AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .LogToTrace();
    }
}
