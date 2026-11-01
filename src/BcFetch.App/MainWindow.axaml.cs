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
    private readonly DispatcherTimer _rowsTimer = new() { Interval = TimeSpan.FromMilliseconds(250) };
    private readonly List<(TrackFile File, ProgressBar Bar, TextBlock Status)> _rows = [];
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
        _rowsTimer.Tick += (_, _) => UpdateTrackRows();

        TextBoxUrls.TextChanged += (_, _) => ButtonStart.Content = Res.buttonStart;
        CheckBoxDownloadDiscography.OnContent = CheckBoxDownloadDiscography.OffContent = Res.checkBoxDownloadDiscography.Replace("_", "");
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

    private void ButtonAbout_Click(object sender, RoutedEventArgs e) => new AboutWindow().ShowDialog(this);

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
            BuildTrackRows(_downloadManager.GetTrackFiles());
            _timer.Start();
            _rowsTimer.Start();
            await _downloadManager.StartDownloadsAsync(_downloadCts.Token);
            var failed = _downloadManager.GetFailedTracksCount();
            if (failed > 0)
            {
                Log($"{failed} track(s) failed. Press \"Retry failed\" to try them again; finished files are skipped.", DownloadProgressChangedLevel.Error);
            }
            ButtonStart.Content = failed > 0 ? "Retry failed" : Res.buttonStart;
            if (_userSettings.NotifyOnFinish) Notifier.Send("bcfetch", failed > 0 ? $"Downloads finished, {failed} track(s) failed" : "Downloads finished");
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
            _rowsTimer.Stop();
            UpdateTrackRows();
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
            TrackList.Children.Clear();
            _rows.Clear();
            TrackPanel.IsVisible = false;
            LabelProgress.Text = "";
        }
        else
        {
            LabelDownloadSpeed.Text = "";
        }
    }

    private void BuildTrackRows(IEnumerable<TrackFile> files)
    {
        var mono = (FontFamily)Avalonia.Application.Current!.FindResource("BcMono")!;
        foreach (var file in files)
        {
            var name = new TextBlock { Text = file.Name, TextTrimming = TextTrimming.CharacterEllipsis, VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center };
            ToolTip.SetTip(name, file.Name);
            var bar = new ProgressBar { Width = 120, Minimum = 0, Maximum = Math.Max(file.Size, 1), VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center };
            bar.Classes.Add("line");
            var status = new TextBlock { FontFamily = mono, FontSize = 12, Width = 64, TextAlignment = TextAlignment.Right, VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center };

            var row = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto,Auto"), ColumnSpacing = 12 };
            Grid.SetColumn(bar, 1);
            Grid.SetColumn(status, 2);
            row.Children.Add(name);
            row.Children.Add(bar);
            row.Children.Add(status);
            TrackList.Children.Add(row);
            _rows.Add((file, bar, status));
        }

        TrackPanel.IsVisible = _rows.Count > 0;
        UpdateTrackRows();
    }

    private void UpdateTrackRows()
    {
        var dark = ActualThemeVariant == Avalonia.Styling.ThemeVariant.Dark;
        foreach (var (file, bar, status) in _rows)
        {
            var state = file.State;
            var sized = file.Size > 0;
            bar.IsIndeterminate = state == TrackFileState.Downloading && !sized;
            bar.Value = state switch
            {
                TrackFileState.Done or TrackFileState.Skipped => bar.Maximum,
                TrackFileState.Downloading => file.BytesReceived,
                _ => 0,
            };
            status.Text = state switch
            {
                TrackFileState.Waiting => "waiting",
                TrackFileState.Downloading => sized ? $"{Math.Min(100, file.BytesReceived * 100 / file.Size)}%" : "…",
                TrackFileState.Done => "done",
                TrackFileState.Skipped => "skipped",
                _ => "failed",
            };
            status.Foreground = new SolidColorBrush(Color.Parse(state switch
            {
                TrackFileState.Failed => dark ? "#FF7A6B" : "#B5392B",
                TrackFileState.Done => dark ? "#5CCB8A" : "#2E7D4F",
                _ => dark ? "#8A9A97" : "#5E6D6B",
            }));
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
        var color = Color.Parse(level switch
        {
            DownloadProgressChangedLevel.VerboseInfo => dark ? "#8A9A97" : "#5E6D6B",
            DownloadProgressChangedLevel.IntermediateSuccess => dark ? "#3FB8C4" : "#0E7C86",
            DownloadProgressChangedLevel.Success => dark ? "#5CCB8A" : "#2E7D4F",
            DownloadProgressChangedLevel.Warning => dark ? "#E3B341" : "#A86B00",
            DownloadProgressChangedLevel.Error => dark ? "#FF7A6B" : "#B5392B",
            _ => dark ? "#E3EAE8" : "#14201F",
        });
        var muted = Color.Parse(dark ? "#8A9A97" : "#5E6D6B");

        LogText.Inlines.Add(new Avalonia.Controls.Documents.Run(DateTime.Now.ToString("HH:mm:ss") + "  ") { Foreground = new SolidColorBrush(muted) });
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
