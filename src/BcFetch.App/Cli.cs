using BcFetch.Bandcamp.Download;
using BcFetch.Core.DependencyInjection;
using BcFetch.Core.Logging;
using BcFetch.Settings;
using Microsoft.Extensions.DependencyInjection;

namespace BcFetch.App;

/// <summary>Headless mode: <c>bcfetch &lt;url&gt;...</c> downloads without opening a window.</summary>
internal static class Cli
{
    private const string Usage = """
        Usage: bcfetch [options] <url>...
               bcfetch                      (no arguments: open the window)

        Options:
          -o, --output <path>   download path template, e.g. "~/Music/{artist}/{album}"
          -d, --discography     download the whole artist discography
          -v, --verbose         show verbose log lines
          -V, --version         print version
          -h, --help            print this help

        Other settings come from ~/.config/bcfetch/bcfetch.ini.
        Exit code: 0 ok, 1 errors, 2 bad usage, 130 cancelled.
        """;

    public static async Task<int> RunAsync(string[] args)
    {
        string output = null;
        bool? discography = null;
        var verbose = false;
        var urls = new List<string>();

        for (var i = 0; i < args.Length; i++)
        {
            switch (args[i])
            {
                case "-h" or "--help": Console.WriteLine(Usage); return 0;
                case "-V" or "--version": Console.WriteLine($"bcfetch {Core.Constants.APP_VERSION_FORMATTED}"); return 0;
                case "-d" or "--discography": discography = true; break;
                case "-v" or "--verbose": verbose = true; break;
                case "-o" or "--output":
                    if (++i >= args.Length) return Fail("--output needs a path");
                    output = args[i];
                    break;
                default:
                    if (args[i].StartsWith('-')) return Fail($"unknown option {args[i]}");
                    urls.Add(args[i]);
                    break;
            }
        }
        if (urls.Count == 0) return Fail("no URL given");

        var container = DependencyInjectionHelper.InitializeContainer();
        container.GetService<ILoggingService>().InitializeLogger();
        var settingsService = container.GetService<ISettingsService>();
        var settings = settingsService.InitializeSettings();

        // Settings write through to the ini file, so restore overrides afterwards.
        var oldPath = settings.DownloadsPath;
        var oldDiscography = settings.DownloadArtistDiscography;
        if (output != null) settings.DownloadsPath = output.StartsWith("~/") ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), output[2..]) : output;
        if (discography != null) settings.DownloadArtistDiscography = discography.Value;

        var manager = container.GetService<IDownloadManager>();
        var failed = false;
        manager.DownloadProgressChanged += (_, e) =>
        {
            if (e.Level == DownloadProgressChangedLevel.Error) failed = true;
            if (e.Level is DownloadProgressChangedLevel.VerboseInfo or DownloadProgressChangedLevel.Warning && !verbose && !settings.ShowVerboseLog) return;
            (e.Level == DownloadProgressChangedLevel.Error ? Console.Error : Console.Out).WriteLine(Clean(e.Message));
        };

        using var cts = new CancellationTokenSource();
        Console.CancelKeyPress += (_, e) => { e.Cancel = true; cts.Cancel(); };

        try
        {
            await manager.InitializeAsync(string.Join('\n', urls), cts.Token);
            await manager.StartDownloadsAsync(cts.Token);
            return failed ? 1 : 0;
        }
        catch (OperationCanceledException)
        {
            Console.Error.WriteLine("Cancelled");
            return 130;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine(Clean(ex.Message));
            return 1;
        }
        finally
        {
            settings.DownloadsPath = oldPath;
            settings.DownloadArtistDiscography = oldDiscography;
        }
    }

    // Titles come from Bandcamp pages; strip control chars (ESC, CR, 0x9B...) so they cannot drive the terminal.
    private static string Clean(string text) => string.Concat(text.Select(c => char.IsControl(c) ? '?' : c));

    private static int Fail(string message)
    {
        Console.Error.WriteLine($"bcfetch: {message}\n\n{Usage}");
        return 2;
    }
}
