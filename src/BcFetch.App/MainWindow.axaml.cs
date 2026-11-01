using System.Diagnostics;
using System.Globalization;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using BcFetch.Bandcamp.Download;
using BcFetch.Core.Logging;
using BcFetch.Helpers;
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
    private List<TrackRowViewModel> _trackRows = [];
    private bool _logScrollPending;
    private const int MaxLogLines = 500;
    private const int LogTrimBatch = 50;
    private CancellationTokenSource _downloadCts;
    private bool _activeDownloads;
    private readonly Queue<string> _queue = new();
    private string _retryInput;
    private TrayIcon _tray;
    private bool _quitting;
    private readonly List<(Avalonia.Controls.Documents.Run Stamp, Avalonia.Controls.Documents.Run Text, DownloadProgressChangedLevel Level)> _logLines = [];
    private readonly CultureInfo _uiCulture = Res.Culture ?? CultureInfo.CurrentUICulture;
    private long _lastTotalReceivedBytes;
    private DateTime _lastSpeedUpdate;

    // Required by the XAML loader / previewer.
    public MainWindow() => InitializeComponent();

    internal MainWindow(ISettingsService settingsService, IDownloadManager downloadManager, IUpdatesService updatesService) : this()
    {
        _settingsService = settingsService;
        _userSettings = settingsService.GetUserSettings();
        LabelAppVersion.Text = Constants.APP_VERSION_FORMATTED;
        _downloadManager = downloadManager;
        _updatesService = updatesService;
        _downloadManager.DownloadProgressChanged += (_, e) => Dispatcher.UIThread.Post(() => Log(e.Message, e.Level));
        _timer.Tick += (_, _) => UpdateProgress();
        _rowsTimer.Tick += (_, _) => UpdateTrackRows();

        TextBoxUrls.TextChanged += (_, _) =>
        {
            if (!_activeDownloads) _retryInput = null;
            UpdateStartLabel();
        };
        CheckBoxDownloadDiscography.OnContent = CheckBoxDownloadDiscography.OffContent = Res.checkBoxDownloadDiscography.Replace("_", "");
        TextBoxDownloadsPath.Text = _userSettings.DownloadsPath;
        TextBoxDownloadsPath.TextChanged += (_, _) => _userSettings.DownloadsPath = TextBoxDownloadsPath.Text;
        CheckBoxDownloadDiscography.IsChecked = _userSettings.DownloadArtistDiscography;
        CheckBoxDownloadDiscography.IsCheckedChanged += (_, _) => _userSettings.DownloadArtistDiscography = CheckBoxDownloadDiscography.IsChecked == true;

Opened += async (_, _) => { if (_userSettings.CheckForUpdates) await CheckForUpdatesAsync(); };
        Closing += OnClosing;

        // The TextBox template fades its placeholder to 50% on the element itself, which a style cannot override and
        // which halves its contrast (2:1); the placeholder colour token is already the quiet tone.
        TextBoxUrls.TemplateApplied += (_, e) =>
        {
            if (e.NameScope.Find<TextBlock>("PART_Placeholder") is { } placeholder)
            {
                placeholder.Opacity = 1;
            }
        };
        ActualThemeVariantChanged += (_, _) =>
        {
            foreach (var (stamp, text, level) in _logLines)
            {
                ColorLogLine(stamp, text, level);
            }

            UpdateTrackRows(force: true);
        };

        // AllowDrop is inherited, so one setting on the window makes the whole window a drop target; the events
        // bubble up from the control under the pointer.
        DragDrop.SetAllowDrop(this, true);
        AddHandler(DragDrop.DragOverEvent, OnDragOver);
        AddHandler(DragDrop.DropEvent, OnDrop);
        SetupTray();
    }

    private void SetupTray()
    {
        var show = new NativeMenuItem(Tr.Get("trayShow"));
        show.Click += (_, _) => ShowFromTray();
        var quit = new NativeMenuItem(Tr.Get("trayQuit"));
        quit.Click += (_, _) =>
        {
            _quitting = true;
            Close();
        };
        var menu = new NativeMenu();
        menu.Add(show);
        menu.Add(quit);

        _tray = new TrayIcon { Icon = Icon, ToolTipText = "bcfetch", Menu = menu, IsVisible = _userSettings.ShowTrayIcon };
        _tray.Clicked += (_, _) => ShowFromTray();
        TrayIcon.SetIcons(Avalonia.Application.Current!, new TrayIcons { _tray });
        Closed += (_, _) => _tray.Dispose();
    }

    private void ShowFromTray()
    {
        Show();
        if (WindowState == WindowState.Minimized)
        {
            WindowState = WindowState.Normal;
        }

        Activate();
    }

    private static void OnDragOver(object sender, DragEventArgs e)
    {
        e.DragEffects = e.DataTransfer.Contains(DataFormat.Text) || e.DataTransfer.Contains(DataFormat.File) ? DragDropEffects.Copy : DragDropEffects.None;
        e.Handled = true;
    }

    // Accepts a dragged link or text, or a text file of URLs.
    private async void OnDrop(object sender, DragEventArgs e)
    {
        e.Handled = true;
        var text = e.DataTransfer.TryGetText() ?? "";
        foreach (var file in e.DataTransfer.TryGetFiles() ?? [])
        {
            var path = file.TryGetLocalPath();
            if (path != null && File.Exists(path) && new FileInfo(path).Length <= 1_000_000)
            {
                text += "\n" + await File.ReadAllTextAsync(path);
            }
        }

        var urls = UrlHelper.ExtractUrls(text);
        if (urls.Count == 0)
        {
            Log(Tr.Get("logNothingDropped"), DownloadProgressChangedLevel.Info);
            return;
        }

        if (_activeDownloads)
        {
            EnqueueUrls(string.Join('\n', urls));
            return;
        }

        var present = UrlHelper.ExtractUrls(TextBoxUrls.Text);
        var added = urls.Where(u => !present.Contains(u)).ToList();
        if (added.Count > 0)
        {
            TextBoxUrls.Text = (string.IsNullOrWhiteSpace(TextBoxUrls.Text) ? "" : TextBoxUrls.Text.TrimEnd() + "\n") + string.Join('\n', added);
        }
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
        _tray.IsVisible = _userSettings.ShowTrayIcon;
        if (_activeDownloads && !Equals(Res.Culture, _uiCulture))
        {
            Log(Tr.Get("logLanguageLater"), DownloadProgressChangedLevel.Info);
        }
        RefreshLanguage();
    }

    /// <summary>
    /// Window texts are resolved when a window is built, so a new language means a new main window with the
    /// same URLs, size and position. Not during a run, and not while "Retry failed" is pending: that state
    /// lives in this window. Settings, About and dialogs are built on demand and follow immediately.
    /// </summary>
    private void RefreshLanguage()
    {
        if (_activeDownloads || _retryInput != null || Equals(Res.Culture, _uiCulture))
        {
            return;
        }

        var next = new MainWindow(_settingsService, _downloadManager, _updatesService)
        {
            Position = Position,
            Width = Width,
            Height = Height,
            WindowState = WindowState,
        };
        next.TextBoxUrls.Text = TextBoxUrls.Text;
        if (Avalonia.Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            desktop.MainWindow = next;
        }

        _quitting = true; // a plain close: no hiding to the tray
        next.Show();
        Close();
    }

    private async void ButtonStart_Click(object sender, RoutedEventArgs e)
    {
        var inputUrls = TextBoxUrls.Text;

        // While a run is active the button adds the pasted URLs to the queue instead.
        if (_activeDownloads)
        {
            if (string.IsNullOrWhiteSpace(inputUrls))
            {
                return;
            }

            EnqueueUrls(inputUrls);
            return;
        }

        // "Retry failed" re-runs the batches that had failures, whatever is in the box now.
        inputUrls = _retryInput ?? inputUrls;
        if (string.IsNullOrWhiteSpace(inputUrls))
        {
            Log("Paste some albums URLs to be downloaded", DownloadProgressChangedLevel.Error);
            return;
        }

        _retryInput = null;
        _downloadCts = new CancellationTokenSource();
        _activeDownloads = true;
        SetDownloading(true);
        var totalFailed = 0;
        var cancelled = false;

        // Run the batch, then every batch queued meanwhile.
        for (var batch = inputUrls; batch != null; batch = _queue.TryDequeue(out var next) ? next : null)
        {
            try
            {
                totalFailed += await RunBatchAsync(batch);
            }
            catch (OperationCanceledException)
            {
                Log("Downloads cancelled by user", DownloadProgressChangedLevel.Info);
                cancelled = true;
                break;
            }
            catch (Exception ex)
            {
                _logger.Error(ex);
                Log(ex.Message, DownloadProgressChangedLevel.Error);
            }
        }

        _queue.Clear();
        _activeDownloads = false;
        SetDownloading(false);
        RefreshLanguage();
        if (!cancelled && _userSettings.NotifyOnFinish)
        {
            Notifier.Send("bcfetch", totalFailed > 0 ? Tr.Get("notifyFinishedFailures", totalFailed) : Tr.Get("notifyFinished"));
        }
    }

    private void EnqueueUrls(string inputUrls)
    {
        _queue.Enqueue(inputUrls.Trim());
        TextBoxUrls.Clear();
        Log(Tr.Get("logQueued", inputUrls.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Length, _queue.Count), DownloadProgressChangedLevel.Info);
    }

    /// <summary>Downloads one batch of URLs and returns how many tracks failed.</summary>
    private async Task<int> RunBatchAsync(string inputUrls)
    {
        ProgressBar.IsIndeterminate = true;
        ProgressBar.Value = 0;
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
                Log(Tr.Get("logTracksFailed", failed, Tr.Get("buttonRetryFailed")), DownloadProgressChangedLevel.Error);
                _retryInput = _retryInput == null ? inputUrls : _retryInput + "\n" + inputUrls;
            }
            return failed;
        }
        finally
        {
            _timer.Stop();
            _rowsTimer.Stop();
            UpdateTrackRows();
            UpdateProgress();
        }
    }

    private async void ButtonStop_Click(object sender, RoutedEventArgs e)
    {
        if (!await Dialogs.ConfirmAsync(this, Res.messageBoxCancelDownloadsText, Res.messageBoxButtonYes, Res.messageBoxButtonNo) || !_activeDownloads)
        {
            return;
        }

        ButtonStop.IsEnabled = false;
        _queue.Clear();
        await _downloadCts.CancelAsync();
    }

    // The one button is Start, Retry failed or (during a run) Add to queue.
    private void UpdateStartLabel() => ButtonStart.Content = _activeDownloads ? Tr.Get("buttonAddToQueue") : _retryInput != null ? Tr.Get("buttonRetryFailed") : Res.buttonStart;

    private void SetDownloading(bool downloading)
    {
        ButtonBrowse.IsEnabled = !downloading;
        ButtonStop.IsEnabled = downloading;
        CheckBoxDownloadDiscography.IsEnabled = !downloading;
        TextBoxDownloadsPath.IsReadOnly = downloading;
        UpdateStartLabel();
        ProgressBar.IsIndeterminate = downloading;
        ProgressBar.Value = 0;
        if (downloading)
        {
            LogText.Inlines.Clear();
            _logLines.Clear();
            ShowTrackPanel(false);
            LabelProgress.Text = "";
        }
        else
        {
            _tray.ToolTipText = "bcfetch";
            LabelDownloadSpeed.Text = "";
        }
    }

    private void BuildTrackRows(IEnumerable<TrackFile> files)
    {
        _trackRows = files.Select(f => new TrackRowViewModel(f)).ToList();
        TrackList.ItemsSource = _trackRows;
        ShowTrackPanel(_trackRows.Count > 0);
        UpdateTrackRows(force: true);
    }

    private void ShowTrackPanel(bool show)
    {
        if (!show)
        {
            TrackList.ItemsSource = null;
            _trackRows = [];
        }

        // The track list takes its share of the height only while it is shown; the log keeps its minimum either way.
        var row = ContentGrid.RowDefinitions[2];
        row.MinHeight = show ? 64 : 0;
        row.MaxHeight = show ? 240 : double.PositiveInfinity;
        row.Height = show ? new GridLength(2, GridUnitType.Star) : new GridLength(0);
        TrackPanel.IsVisible = show;
    }

    private void UpdateTrackRows(bool force = false)
    {
        foreach (var row in _trackRows)
        {
            row.Update(this.Token, force);
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

        if (!ProgressBar.IsIndeterminate && ProgressBar.Maximum > 0)
        {
            _tray.ToolTipText = $"bcfetch {ProgressBar.Value * 100 / ProgressBar.Maximum:0}%";
        }

        LabelProgress.Text = (received / (1024.0 * 1024)).ToString("0.00") + " MB" +
                             (_userSettings.RetrieveFilesSize ? " / " + (_downloadManager.GetTotalBytesToDownload() / (1024.0 * 1024)).ToString("0.00") + " MB" : "");
        ProgressBar.Value = _userSettings.RetrieveFilesSize ? received : _downloadManager.GetTotalFilesCountReceived();
    }

    // Colours come from the theme tokens by level, so lines already written follow a theme switch.
    private void ColorLogLine(Avalonia.Controls.Documents.Run stamp, Avalonia.Controls.Documents.Run text, DownloadProgressChangedLevel level)
    {
        stamp.Foreground = this.Token("BcMuted");
        text.Foreground = this.Token(level switch
        {
            DownloadProgressChangedLevel.VerboseInfo => "BcMuted",
            DownloadProgressChangedLevel.IntermediateSuccess => "BcAccent",
            DownloadProgressChangedLevel.Success => "BcSuccess",
            DownloadProgressChangedLevel.Warning => "BcWarning",
            DownloadProgressChangedLevel.Error => "BcDanger",
            _ => "BcText",
        });
    }

    private void Log(string message, DownloadProgressChangedLevel level)
    {
        _logger.Log(level.ToNLogLevel(), message);

        if (!_userSettings.ShowVerboseLog && level is DownloadProgressChangedLevel.VerboseInfo or DownloadProgressChangedLevel.Warning)
        {
            return;
        }

        var stamp = new Avalonia.Controls.Documents.Run(DateTime.Now.ToString("HH:mm:ss") + "  ");
        var text = new Avalonia.Controls.Documents.Run(message + Environment.NewLine);
        LogText.Inlines.Add(stamp);
        LogText.Inlines.Add(text);
        _logLines.Add((stamp, text, level));
        ColorLogLine(stamp, text, level);

        // Re-laying out a long log on every line gets slower as it grows (3,000 lines took 11 s), so the window keeps
        // the latest lines only; the full history is always in the log file. Trimming in batches keeps it cheap.
        if (_logLines.Count > MaxLogLines + LogTrimBatch)
        {
            LogText.Inlines.RemoveRange(0, LogTrimBatch * 2);
            _logLines.RemoveRange(0, LogTrimBatch);
        }

        // One scroll per frame, however many lines arrived in it
        if (!_logScrollPending)
        {
            _logScrollPending = true;
            Dispatcher.UIThread.Post(() =>
            {
                _logScrollPending = false;
                LogScroll.ScrollToEnd();
            }, DispatcherPriority.Background);
        }
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
        if (!_quitting && _userSettings.ShowTrayIcon && _userSettings.CloseToTray)
        {
            e.Cancel = true;
            Hide();
            return;
        }

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
        else
        {
            _quitting = false;
        }
    }
}
