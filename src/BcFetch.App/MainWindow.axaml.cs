using System.Diagnostics;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using BcFetch.Bandcamp.Download;
using BcFetch.Core.Logging;
using BcFetch.Core;
using BcFetch.Core.Updates;
using BcFetch.Settings;
using NLog;
using Res = BcFetch.App.Properties.Resources;

namespace BcFetch.App;

internal sealed partial class MainWindow : Window
{
    private readonly ISettingsService _settingsService;
    private readonly IUserSettings _userSettings;
    private readonly IDownloadManager _downloadManager;
    private readonly IUpdatesService _updatesService;
    private readonly Logger _logger = LogManager.GetCurrentClassLogger();
    private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromSeconds(1) };
    private CancellationTokenSource _downloadCts;
    private bool _activeDownloads;
    private long _lastTotalReceivedBytes;
    private DateTime _lastSpeedUpdate;

    // Required by the XAML loader / previewer.
    public MainWindow() => InitializeComponent();

    internal MainWindow(ISettingsService settingsService, IDownloadManager downloadManager, IUpdatesService updatesService) : this()
    {
        _settingsService = settingsService;
        _userSettings = settingsService.GetUserSettings();
        _downloadManager = downloadManager;
        _updatesService = updatesService;
        _downloadManager.DownloadProgressChanged += (_, e) => Dispatcher.UIThread.Post(() => Log(e.Message, e.Level));
        _timer.Tick += (_, _) => UpdateProgress();

        TextBoxDownloadsPath.Text = _userSettings.DownloadsPath;
        TextBoxDownloadsPath.TextChanged += (_, _) => _userSettings.DownloadsPath = TextBoxDownloadsPath.Text;
        CheckBoxDownloadDiscography.IsChecked = _userSettings.DownloadArtistDiscography;
        CheckBoxDownloadDiscography.IsCheckedChanged += (_, _) => _userSettings.DownloadArtistDiscography = CheckBoxDownloadDiscography.IsChecked == true;

        Opened += async (_, _) => { if (_userSettings.CheckForUpdates) await CheckForUpdatesAsync(); };
        Closing += OnClosing;
    }

    private async void ButtonBrowse_Click(object sender, RoutedEventArgs e)
    {
        var folders = await StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions { AllowMultiple = false });
        if (folders.Count > 0 && folders[0].TryGetLocalPath() is { } path)
        {
            TextBoxDownloadsPath.Text = Path.Combine(path, "{artist}", "{album}");
        }
    }

    private async void ButtonOpenSettingsWindow_Click(object sender, RoutedEventArgs e)
    {
        await new SettingsWindow(_settingsService, _activeDownloads).ShowDialog(this);
        // The window edits the same settings object; refresh the controls shown on this window.
        TextBoxDownloadsPath.Text = _userSettings.DownloadsPath;
        CheckBoxDownloadDiscography.IsChecked = _userSettings.DownloadArtistDiscography;
    }

    private async void ButtonStart_Click(object sender, RoutedEventArgs e)
    {
        var inputUrls = TextBoxUrls.Text;
        if (string.IsNullOrWhiteSpace(inputUrls))
        {
            Log("Paste some albums URLs to be downloaded", DownloadProgressChangedLevel.Error);
            return;
        }

        _downloadCts = new CancellationTokenSource();
        _activeDownloads = true;
        SetDownloading(true);
        _logger.Info($"Starting download with the following input URLs:{Environment.NewLine}{inputUrls}");
        Log("Starting download...", DownloadProgressChangedLevel.Info);

        try
        {
            await _downloadManager.InitializeAsync(inputUrls, _downloadCts.Token);

            var max = _userSettings.RetrieveFilesSize ? _downloadManager.GetTotalBytesToDownload() : _downloadManager.GetTotalFilesCountToDownload();
            if (max > 0)
            {
                ProgressBar.IsIndeterminate = false;
                ProgressBar.Maximum = max;
            }

            _lastTotalReceivedBytes = 0;
            _lastSpeedUpdate = DateTime.Now;
            _timer.Start();
            await _downloadManager.StartDownloadsAsync(_downloadCts.Token);
        }
        catch (OperationCanceledException)
        {
            Log("Downloads cancelled by user", DownloadProgressChangedLevel.Info);
        }
        catch (Exception ex)
        {
            _logger.Error(ex);
            Log(ex.Message, DownloadProgressChangedLevel.Error);
        }
        finally
        {
            _timer.Stop();
            UpdateProgress();
            _activeDownloads = false;
            SetDownloading(false);
        }
    }

    private async void ButtonStop_Click(object sender, RoutedEventArgs e)
    {
        if (!await Dialogs.ConfirmAsync(this, Res.messageBoxCancelDownloadsText, Res.messageBoxButtonYes, Res.messageBoxButtonNo) || !_activeDownloads)
        {
            return;
        }

        ButtonStop.IsEnabled = false;
        await _downloadCts.CancelAsync();
    }

    private void SetDownloading(bool downloading)
    {
        ButtonBrowse.IsEnabled = !downloading;
        ButtonStart.IsEnabled = !downloading;
        ButtonStop.IsEnabled = downloading;
        CheckBoxDownloadDiscography.IsEnabled = !downloading;
        TextBoxDownloadsPath.IsReadOnly = downloading;
        TextBoxUrls.IsReadOnly = downloading;
        ProgressBar.IsIndeterminate = downloading;
        ProgressBar.Value = 0;
        if (downloading)
        {
            LogText.Inlines.Clear();
            LabelProgress.Text = "";
        }
        else
        {
            LabelDownloadSpeed.Text = "";
        }
    }

    private void UpdateProgress()
    {
        var received = _downloadManager.GetTotalBytesReceived();
        var now = DateTime.Now;
        var seconds = (now - _lastSpeedUpdate).TotalSeconds;
        if (seconds > 0)
        {
            LabelDownloadSpeed.Text = ((received - _lastTotalReceivedBytes) / seconds / 1024).ToString("0.0") + " kB/s";
        }
        _lastTotalReceivedBytes = received;
        _lastSpeedUpdate = now;

        LabelProgress.Text = (received / (1024.0 * 1024)).ToString("0.00") + " MB" +
                             (_userSettings.RetrieveFilesSize ? " / " + (_downloadManager.GetTotalBytesToDownload() / (1024.0 * 1024)).ToString("0.00") + " MB" : "");
        ProgressBar.Value = _userSettings.RetrieveFilesSize ? received : _downloadManager.GetTotalFilesCountReceived();
    }

    private void Log(string message, DownloadProgressChangedLevel level)
    {
        _logger.Log(level.ToNLogLevel(), message);

        if (!_userSettings.ShowVerboseLog && level is DownloadProgressChangedLevel.VerboseInfo or DownloadProgressChangedLevel.Warning)
        {
            return;
        }

        var dark = ActualThemeVariant == Avalonia.Styling.ThemeVariant.Dark;
        var color = level switch
        {
            DownloadProgressChangedLevel.VerboseInfo or DownloadProgressChangedLevel.IntermediateSuccess => dark ? Colors.LightSkyBlue : Colors.MediumBlue,
            DownloadProgressChangedLevel.Success => dark ? Colors.Lime : Colors.Green,
            DownloadProgressChangedLevel.Warning => dark ? Colors.Orange : Colors.OrangeRed,
            DownloadProgressChangedLevel.Error => Colors.Red,
            _ => dark ? Colors.WhiteSmoke : Colors.Black,
        };

        LogText.Inlines.Add(new Avalonia.Controls.Documents.Run(DateTime.Now.ToString("HH:mm:ss") + " ") { Foreground = Brushes.Gray });
        LogText.Inlines.Add(new Avalonia.Controls.Documents.Run(message + Environment.NewLine) { Foreground = new SolidColorBrush(color) });
        LogScroll.ScrollToEnd();
    }

    private async Task CheckForUpdatesAsync()
    {
        try
        {
            if ((await _updatesService.GetLatestVersionAsync()).IsNewerVersion())
            {
                LabelNewVersion.Text = Res.labelVersionNewUpdateAvailable;
                LabelNewVersion.IsVisible = true;
            }
        }
        catch (Exception ex)
        {
            // Not an error for the user (e.g. no release published yet): log only.
            _logger.Warn(ex, "Failed to get latest version");
        }
    }

    private void LabelNewVersion_PointerPressed(object sender, PointerPressedEventArgs e)
    {
        Process.Start(new ProcessStartInfo(Constants.URL_LATEST_RELEASE) { UseShellExecute = true });
    }

    private async void OnClosing(object sender, WindowClosingEventArgs e)
    {
        if (!_activeDownloads)
        {
            return;
        }

        e.Cancel = true;
        if (await Dialogs.ConfirmAsync(this, Res.messageBoxCloseWindowWhenDownloadingText, Res.messageBoxCloseWindowWhenDownloadingButtonOk, Res.messageBoxButtonCancel))
        {
            _activeDownloads = false;
            await _downloadCts.CancelAsync();
            Close();
        }
    }
}
