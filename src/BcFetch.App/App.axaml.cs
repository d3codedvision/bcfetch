using System.Globalization;
using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Avalonia.Styling;
using BcFetch.Core.DependencyInjection;
using BcFetch.Core.Logging;
using BcFetch.Settings;
using Microsoft.Extensions.DependencyInjection;
using NLog;

namespace BcFetch.App;

public sealed class App : Application
{
    public override void Initialize() => AvaloniaXamlLoader.Load(this);

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            var container = DependencyInjectionHelper.InitializeContainer();
            container.GetService<ILoggingService>().InitializeLogger();
            AppDomain.CurrentDomain.UnhandledException += (_, e) => LogManager.GetCurrentClassLogger().Fatal(e.ExceptionObject as Exception, "Unhandled exception");

            var settings = container.GetService<ISettingsService>().InitializeSettings();
            ApplyLanguage(settings.Language);
            RequestedThemeVariant = ThemeTokens.VariantOf(settings.Theme);

            LogManager.GetCurrentClassLogger().Info($"bcfetch {Core.Constants.APP_VERSION_FORMATTED}, settings: {container.GetService<ISettingsService>().GetUserSettingsInJson()}");

            desktop.MainWindow = new MainWindow(container.GetService<ISettingsService>(), container.GetService<Bandcamp.Download.IDownloadManager>(), container.GetService<Core.Updates.IUpdatesService>());
        }

        base.OnFrameworkInitializationCompleted();
    }

    internal static void ApplyLanguage(Language language)
    {
        // Enum names mirror culture names, with '_' for '-' (e.g. pt_BR).
        var culture = new CultureInfo(language.ToString().Replace('_', '-'));
        Properties.Resources.Culture = culture;
        CultureInfo.CurrentUICulture = culture;
    }
}
