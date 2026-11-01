using Avalonia;
using Avalonia.Media;

namespace BcFetch.App;

internal static class Program
{
    [STAThread]
    public static int Main(string[] args) => args.Length > 0 ? Cli.RunAsync(args).GetAwaiter().GetResult() : BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);

    public static AppBuilder BuildAvaloniaApp() => AppBuilder.Configure<App>().UsePlatformDetect().With(new FontManagerOptions { DefaultFamilyName = "avares://bcfetch/Assets/Fonts#Outfit Gothic" }).LogToTrace();
}
