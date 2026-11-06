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
    private int _logErrors;
    private ScrollViewer _trackScroll;
    private int _followIndex = -1;
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
    private DateTime _batchStart;
    private string _programmaticUrls; // text the code put into the URL box, so its change is not taken as typing

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
            // The change events arrive later than the assignment, so compare the text instead of using a flag
            if (_programmaticUrls != null && (TextBoxUrls.Text ?? "") == _programmaticUrls)
            {
                _programmaticUrls = null;
                return;
            }

            if (!_activeDownloads) _retryInput = null;
            UpdateStartLabel();
        };
        CheckBoxDownloadDiscography.OnContent = CheckBoxDownloadDiscography.OffContent = Res.checkBoxDownloadDiscography.Replace("_", "");
        UpdateSaveTo();
        TabTracks.Header = Tr.Get("tabTracks");
        TabLog.Header = Tr.Get("tabLog");
        TabDetails.SelectedItem = TabLog; // the first-steps hint lives in the log until a run starts
        ButtonClear.Content = Tr.Get("buttonClear");
        CheckBoxDownloadDiscography.IsChecked = _userSettings.DownloadArtistDiscography;
        CheckBoxDownloadDiscography.IsCheckedChanged += (_, _) => _userSettings.DownloadArtistDiscography = CheckBoxDownloadDiscography.IsChecked == true;

Opened += async (_, _) => { if (_userSettings.CheckForUpdates) await CheckForUpdatesAsync(); };
        Closing += OnClosing;

        // The TextBox template fades its placeholder to 50% on the element itself, which a style cannot override and
        // which halves its contrast (2:1); the placeholder colour token is already the quiet tone.
        // Empty state of the log panel; shown until the first line is logged
        var discography = Res.checkBoxDownloadDiscography.Replace("_", "");
        EmptyStep1.Text = Tr.Get("emptyStep1");
        EmptyStep2.Text = Tr.Get("emptyStep2");
        EmptyStep3.Text = Tr.Get("emptyStep3", Res.buttonStart.Replace("_", ""));
        EmptyExample.Text = Tr.Get("emptyExample", "https://artist.bandcamp.com/album/name");
        EmptyDiscography.Text = Tr.Get("emptyDiscography", discography);

        KeyboardScrolling.Enable(LogScroll);
        TrackList.TemplateApplied += (_, e) =>
        {
            if (e.NameScope.Find<ScrollViewer>("PART_TrackScroll") is { } trackScroll)
            {
                _trackScroll = trackScroll;
                KeyboardScrolling.Enable(trackScroll);
            }
        };
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
            RefreshTrackSummary();
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

    private void UpdateSaveTo()
    {
        LabelSaveTo.Text = _userSettings.AskDownloadsFolderEveryTime ? Tr.Get("labelSaveToAsk") : Tr.Get("labelSaveTo", _userSettings.DownloadsPath);
        ToolTip.SetTip(LabelSaveTo, Tr.Get("labelSaveToTip"));
    }

    private async void LabelSaveTo_PointerPressed(object sender, PointerPressedEventArgs e)
    {
        if (!_activeDownloads)
        {
            await OpenSettingsAsync("Downloads");
        }
    }

    /// <summary>Empties the links box, the track list and the log.</summary>
    private void ButtonClear_Click(object sender, RoutedEventArgs e)
    {
        SetUrlsText("");
        _retryInput = null;
        ShowTrackPanel(false);
        LogText.Inlines.Clear();
        _logLines.Clear();
        _logErrors = 0;
        TabLog.Header = Tr.Get("tabLog");
        EmptyState.IsVisible = true;
        LabelProgress.Text = "";
        LabelDownloadSpeed.Text = "";
        TabDetails.SelectedItem = TabLog;
        UpdateStartLabel();
    }

    private void ButtonAbout_Click(object sender, RoutedEventArgs e) => new AboutWindow().ShowDialog(this);

    private async void ButtonOpenSettingsWindow_Click(object sender, RoutedEventArgs e) => await OpenSettingsAsync(null);

    private async Task OpenSettingsAsync(string tab)
    {
        await new SettingsWindow(_settingsService, _activeDownloads, tab).ShowDialog(this);
        // The window edits the same settings object; refresh the controls shown on this window.
        UpdateSaveTo();
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

        // "Ask before each download": the chosen folder is used for this run only (all its queued batches too)
        string savedPath = null;
        if (_userSettings.AskDownloadsFolderEveryTime)
        {
            var folders = await StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions { Title = Tr.Get("pickFolderTitle") });
            if (folders.Count == 0 || folders[0].TryGetLocalPath() is not { } picked)
            {
                return;
            }

            savedPath = _userSettings.DownloadsPath;
            _userSettings.DownloadsPath = Path.Combine(picked, "{artist}", "{album}");
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
        if (savedPath != null)
        {
            _userSettings.DownloadsPath = savedPath;
        }

        _activeDownloads = false;
        SetDownloading(false);
        RefreshLanguage();
        if (!cancelled)
        {
            Announce(totalFailed > 0 ? Tr.Get("notifyFinishedFailures", totalFailed) : Tr.Get("notifyFinished"));
        }

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
            _lastSpeedUpdate = _batchStart = DateTime.Now;
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
            if (_trackRows.Count > 0)
            {
                FinishTrackList();
            }

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

    /// <summary>Has screen readers read a message (the element is a live region, invisible on screen).</summary>
    private void Announce(string message)
    {
        LabelAnnouncement.Text = ""; // changing the text is what triggers the announcement, so repeat messages need a reset
        LabelAnnouncement.Text = message;
    }

    // The one button is Start, Retry failed or (during a run) Add to queue.
    private void SetUrlsText(string text)
    {
        if ((TextBoxUrls.Text ?? "") == text)
        {
            return; // no change event will follow, so there is nothing to mark
        }

        _programmaticUrls = text;
        TextBoxUrls.Text = text;
    }

    private void UpdateStartLabel()
    {
        ButtonStart.Content = _activeDownloads ? Tr.Get("buttonAddToQueue") : _retryInput != null ? Tr.Get("buttonRetryFailed") : Res.buttonStart;

        // During a run the button only queues more links: a quiet button, so no filled primary competes with Cancel
        ButtonStart.Classes.Set("primary", !_activeDownloads);
        TextBoxUrls.PlaceholderText = _activeDownloads ? Tr.Get("urlsPlaceholderQueue") : Res.urlsPlaceholder;
    }

    private void SetDownloading(bool downloading)
    {
        TextBoxUrls.IsEnabled = !downloading;
        ButtonClear.IsEnabled = !downloading;
        ButtonStart.IsEnabled = !downloading; // links are added before a run (or dropped on the window to queue them)
        ButtonStop.IsVisible = downloading; // nothing to cancel otherwise, so no disabled button
        ButtonStop.IsEnabled = downloading;
        CheckBoxDownloadDiscography.IsEnabled = !downloading;
        LabelSaveTo.Opacity = downloading ? 0.6 : 1;
        UpdateStartLabel();
        ProgressBar.IsIndeterminate = downloading;
        ProgressBar.Value = 0;
        if (downloading)
        {
            LogText.Inlines.Clear();
            _logLines.Clear();
            ShowTrackPanel(false);
            TabDetails.SelectedItem = TabTracks;
            _logErrors = 0;
            TabLog.Header = Tr.Get("tabLog");
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
        TrackSummary.IsVisible = false;
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

        TrackSummary.IsVisible = false;

        _followIndex = -1;
    }

    /// <summary>
    /// At the end of a batch: failed tracks move to the top, so they are never hidden below the fold of a long
    /// list (the sort is stable), and a one-line result strip shows the counts.
    /// </summary>
    private void FinishTrackList()
    {
        _trackRows = _trackRows.OrderBy(r => r.Failed ? 0 : 1).ToList();
        TrackList.ItemsSource = _trackRows;
        TrackSummary.IsVisible = true;
        RefreshTrackSummary();
    }

    private void RefreshTrackSummary()
    {
        if (!TrackSummary.IsVisible)
        {
            return;
        }

        var inlines = TrackSummary.Inlines!;
        inlines.Clear();
        void Part(string text, string token)
        {
            if (inlines.Count > 0)
            {
                inlines.Add(new Avalonia.Controls.Documents.Run(" · ") { Foreground = this.Token("BcMuted") });
            }

            inlines.Add(new Avalonia.Controls.Documents.Run(text) { Foreground = this.Token(token) });
        }

        var done = _trackRows.Count(r => r.State == TrackFileState.Done);
        var skipped = _trackRows.Count(r => r.State == TrackFileState.Skipped);
        var failed = _trackRows.Count(r => r.Failed);
        if (done > 0) Part(Tr.Get("summaryDone", done), "BcSuccess");
        if (skipped > 0) Part(Tr.Get("summarySkipped", skipped), "BcMuted");
        if (failed > 0) Part(Tr.Get("summaryFailed", failed), "BcDanger");
    }

    private void UpdateTrackRows(bool force = false)
    {
        foreach (var row in _trackRows)
        {
            row.Update(this.Token, force);
        }

        FollowActiveTrack();
    }

    /// <summary>
    /// While a run is active the list scrolls to keep the newest track in progress (else the next waiting one) in view.
    /// It moves only when that track changes, so scrolling by hand in between is not fought.
    /// </summary>
    private void FollowActiveTrack()
    {
        if (!_activeDownloads || _trackScroll == null || _trackRows.Count == 0)
        {
            return;
        }

        var index = _trackRows.FindLastIndex(r => r.State == TrackFileState.Downloading);
        if (index < 0)
        {
            index = _trackRows.FindIndex(r => r.State == TrackFileState.Waiting);
        }

        var rowHeight = _trackScroll.Extent.Height / _trackRows.Count;
        if (index < 0 || index == _followIndex || rowHeight <= 0)
        {
            return;
        }

        _followIndex = index;
        var top = index * rowHeight;
        var offset = _trackScroll.Offset.Y;
        var view = _trackScroll.Viewport.Height;
        if (top < offset)
        {
            _trackScroll.Offset = new Avalonia.Vector(0, top);
        }
        else if (top + rowHeight > offset + view)
        {
            _trackScroll.Offset = new Avalonia.Vector(0, top + rowHeight - view);
        }
    }

    private void UpdateProgress()
    {
        const double MB = 1024.0 * 1024;
        var received = _downloadManager.GetTotalBytesReceived();
        var total = _userSettings.RetrieveFilesSize ? _downloadManager.GetTotalBytesToDownload() : 0;
        var now = DateTime.Now;
        var seconds = (now - _lastSpeedUpdate).TotalSeconds;
        var kbPerSecond = seconds > 0 ? (received - _lastTotalReceivedBytes) / seconds / 1024 : 0;
        _lastTotalReceivedBytes = received;
        _lastSpeedUpdate = now;

        // Time left at the average speed of this batch, steadier than the last second; only when the total is known
        string left = null;
        var elapsed = (now - _batchStart).TotalSeconds;
        if (total > 0 && received > 0 && received < total && elapsed >= 3)
        {
            var remaining = (total - received) / (received / elapsed);
            left = remaining < 90 ? Tr.Get("progressTimeLeftSeconds", (int)Math.Ceiling(remaining)) : Tr.Get("progressTimeLeftMinutes", (int)Math.Ceiling(remaining / 60));
        }

        var speed = kbPerSecond >= 1024 ? $"{kbPerSecond / 1024:0.0} MB/s" : $"{kbPerSecond:0} kB/s";
        LabelDownloadSpeed.Text = left == null ? speed : $"{speed} · {left}";

        // One readout for the whole run: tracks settled, then bytes
        var size = total > 0 ? $"{received / MB:0.0} / {total / MB:0.0} MB" : $"{received / MB:0.0} MB";
        LabelProgress.Text = _trackRows.Count > 0 ? $"{Tr.Get("progressTracks", _trackRows.Count(r => r.Settled), _trackRows.Count)} · {size}" : size;

        ProgressBar.Value = _userSettings.RetrieveFilesSize ? received : _downloadManager.GetTotalFilesCountReceived();
        if (!ProgressBar.IsIndeterminate && ProgressBar.Maximum > 0)
        {
            _tray.ToolTipText = $"bcfetch {ProgressBar.Value * 100 / ProgressBar.Maximum:0}%";
        }
    }

    // Colours come from the theme tokens by level, so lines already written follow a theme switch.
    // Success is green everywhere (log and track list); the accent colour stays for actions and progress.
    private void ColorLogLine(Avalonia.Controls.Documents.Run stamp, Avalonia.Controls.Documents.Run text, DownloadProgressChangedLevel level)
    {
        stamp.Foreground = this.Token("BcMuted");
        text.Foreground = this.Token(level switch
        {
            DownloadProgressChangedLevel.VerboseInfo => "BcMuted",
            DownloadProgressChangedLevel.IntermediateSuccess or DownloadProgressChangedLevel.Success => "BcSuccess",
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
        EmptyState.IsVisible = false;
        if (level == DownloadProgressChangedLevel.Error)
        {
            TabLog.Header = Tr.Get("tabLogErrors", ++_logErrors);
        }

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
