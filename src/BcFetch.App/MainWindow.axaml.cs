using System.Diagnostics;
using System.Globalization;
using Avalonia.Automation;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Input;
using Avalonia.Input.Platform;
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
    private List<TrackRowViewModel> _items = []; // what the list shows: an album header, then its tracks, album after album
    private bool _logScrollPending;
    private int _logErrors;
    private ScrollViewer _trackScroll;
    private string _undoUrls;
    private string _runFolder;
    private readonly DispatcherTimer _undoTimer = new() { Interval = TimeSpan.FromSeconds(5) };
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
        _downloadManager.DownloadProgressChanged += (_, e) => Dispatcher.UIThread.Post(() => Log(e.Message, e.Level, e.Url));
        _timer.Tick += (_, _) => UpdateProgress();
        _rowsTimer.Tick += (_, _) => UpdateTrackRows();
        _undoTimer.Tick += (_, _) => ExpireUndo();

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
        SetLogHeader();
        ButtonClear.Content = Tr.Get("buttonClear");
        LabelQueueHint.Text = Tr.Get("queueHint");
        ButtonOpenFolder.Content = Tr.Get("buttonOpenFolder");
        UpdateStartLabel();
        UpdateEmptyState();
        CheckBoxDownloadDiscography.IsChecked = _userSettings.DownloadArtistDiscography;
        CheckBoxDownloadDiscography.IsCheckedChanged += (_, _) => _userSettings.DownloadArtistDiscography = CheckBoxDownloadDiscography.IsChecked == true;

Opened += async (_, _) => { if (_userSettings.CheckForUpdates) await CheckForUpdatesAsync(); };
        Closing += OnClosing;
        Activated += async (_, _) => await OfferClipboardAsync();
        AddHandler(KeyDownEvent, OnShortcut, RoutingStrategies.Tunnel); // tunnel: the links box would otherwise take Ctrl+Enter
        ToolTip.SetTip(ButtonStart, Tr.Get("shortcutStart"));
        ToolTip.SetTip(ButtonStop, Tr.Get("shortcutCancel"));
        ToolTip.SetTip(ButtonClear, Tr.Get("shortcutClear"));
        RestoreWindowGeometry();

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
            SetLogHeader();
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
        // In "ask" mode the folder picked for the run in progress is shown while it runs
        LabelSaveTo.Text = !_userSettings.AskDownloadsFolderEveryTime ? Tr.Get("labelSaveTo", _userSettings.DownloadsPath)
            : _runFolder != null ? Tr.Get("labelSaveTo", _runFolder) : Tr.Get("labelSaveToAsk");
        ToolTip.SetTip(ButtonSaveTo, Tr.Get("labelSaveToTip"));
        AutomationProperties.SetName(ButtonSaveTo, LabelSaveTo.Text + ". " + Tr.Get("labelSaveToTip"));
    }

    // Bandcamp links on the clipboard that are not in the box yet; offered as a button over the box.
    private List<string> _clipboardLinks = [];
    private string _clipboardDismissed;

    private async Task OfferClipboardAsync()
    {
        ButtonPasteLinks.IsVisible = false;
        if (_activeDownloads || Clipboard == null)
        {
            return;
        }

        string text;
        try
        {
            text = await Clipboard.TryGetTextAsync();
        }
        catch (Exception ex)
        {
            _logger.Debug(ex, "Could not read the clipboard");
            return;
        }

        if (text == null || text == _clipboardDismissed)
        {
            return;
        }

        var present = UrlHelper.ExtractUrls(TextBoxUrls.Text);
        _clipboardLinks = UrlHelper.ExtractUrls(text)
            .Where(u => Uri.TryCreate(u, UriKind.Absolute, out var uri) && uri.Host.EndsWith("bandcamp.com", StringComparison.OrdinalIgnoreCase) && !present.Contains(u))
            .ToList();
        if (_clipboardLinks.Count > 0)
        {
            ButtonPasteLinks.Content = Tr.Get(_clipboardLinks.Count == 1 ? "buttonPasteLink" : "buttonPasteLinks", _clipboardLinks.Count);
            _clipboardDismissed = text; // offered once per clipboard content: after a click, or after it was ignored and the window lost focus
            ButtonPasteLinks.IsVisible = true;
        }
    }

    private void ButtonPasteLinks_Click(object sender, RoutedEventArgs e)
    {
        TextBoxUrls.Text = (string.IsNullOrWhiteSpace(TextBoxUrls.Text) ? "" : TextBoxUrls.Text.TrimEnd() + "\n") + string.Join('\n', _clipboardLinks);
        ButtonPasteLinks.IsVisible = false;
        TextBoxUrls.Focus();
    }

    private async void ButtonSaveTo_Click(object sender, RoutedEventArgs e) => await OpenSettingsAsync("Downloads");

    /// <summary>Empties the links box, the track list and the log.</summary>
    private void ButtonClear_Click(object sender, RoutedEventArgs e)
    {
        // The button turns into Undo for a few seconds, so pasted links are never lost by one click
        if (_undoUrls != null)
        {
            TextBoxUrls.Text = _undoUrls;
            ExpireUndo();
            return;
        }

        if (!string.IsNullOrWhiteSpace(TextBoxUrls.Text))
        {
            _undoUrls = TextBoxUrls.Text;
            ButtonClear.Content = Tr.Get("buttonUndoClear");
            _undoTimer.Stop();
            _undoTimer.Start();
        }

        SetUrlsText("");
        _retryInput = null;
        ShowTrackPanel(false);
        LogText.Inlines.Clear();
        _logLines.Clear();
        _logErrors = 0;
        SetLogHeader();
        LabelProgress.Text = "";
        LabelDownloadSpeed.Text = "";
        TabDetails.SelectedItem = TabTracks;
        UpdateStartLabel();
    }

    // The Log tab title turns into the danger colour once the run has logged errors, and counts them.
    private void SetLogHeader()
    {
        TabLog.Header = _logErrors == 0 ? Tr.Get("tabLog") : new TextBlock { Text = Tr.Get("tabLogErrors", _logErrors), Foreground = this.Token("BcDanger") };
    }

    private void ExpireUndo()
    {
        _undoTimer.Stop();
        _undoUrls = null;
        ButtonClear.Content = Tr.Get("buttonClear");
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

            _runFolder = picked;
            UpdateSaveTo();
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

        _runFolder = null;
        UpdateSaveTo();

        _activeDownloads = false;
        SetDownloading(false);
        if (_trackRows.Count == 0 && _logErrors > 0)
        {
            TabDetails.SelectedItem = TabLog; // the run failed before any track was listed: the reason is in the log
        }

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

    // The one button is Download or Retry failed; it is hidden during a run and disabled while there is nothing to start.
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
        ButtonStart.Content = _retryInput != null ? Tr.Get("buttonRetryFailed") : Res.buttonStart;
        ButtonStart.IsEnabled = _retryInput != null || !string.IsNullOrWhiteSpace(TextBoxUrls.Text);
        ButtonStart.IsVisible = !_activeDownloads; // Cancel takes its place; links dropped on the window are queued
        LabelQueueHint.IsVisible = _activeDownloads;
    }

    private void SetDownloading(bool downloading)
    {
        TextBoxUrls.IsEnabled = !downloading;
        ButtonClear.IsEnabled = !downloading;
        ButtonStop.IsVisible = downloading; // nothing to cancel otherwise, so no disabled button
        ButtonStop.IsEnabled = downloading;
        CheckBoxDownloadDiscography.IsEnabled = !downloading;
        ButtonSaveTo.IsEnabled = !downloading;
        UpdateStartLabel();
        ProgressBar.IsIndeterminate = downloading;
        ProgressBar.Value = 0;
        if (downloading)
        {
            ExpireUndo();
            LogText.Inlines.Clear();
            _logLines.Clear();
            ShowTrackPanel(false);
            TabDetails.SelectedItem = TabTracks;
            _logErrors = 0;
            SetLogHeader();
            ButtonPasteLinks.IsVisible = false;
            LabelProgress.Text = "";
        }
        else
        {
            _tray.ToolTipText = "bcfetch";
            LabelDownloadSpeed.Text = "";
        }
        UpdateEmptyState();
    }

    private void BuildTrackRows(IEnumerable<TrackFile> files)
    {
        TrackSummary.IsVisible = false;
        _trackRows = files.Select(f => new TrackRowViewModel(f)).ToList();
        ButtonOpenFolder.IsVisible = false;
        ShowItems(failedFirst: false);
        ShowTrackPanel(_trackRows.Count > 0);
        UpdateTrackRows(force: true);
    }

    /// <summary>
    /// Builds the list: tracks grouped by album under a header that counts them. At the end of a batch the albums
    /// with failures come first, and the failed tracks first within an album, so none hides below the fold.
    /// </summary>
    private void ShowItems(bool failedFirst)
    {
        var groups = _trackRows.GroupBy(r => r.Album).ToList(); // GroupBy keeps first-appearance order
        if (failedFirst)
        {
            groups = groups.OrderBy(g => g.Any(r => r.Failed) ? 0 : 1).ToList();
        }

        _items = [];
        foreach (var group in groups)
        {
            var tracks = group.OrderBy(r => failedFirst && r.Failed ? 0 : 1).ThenBy(r => r.Display, StringComparer.OrdinalIgnoreCase).ToList();
            _items.Add(new TrackRowViewModel(group.Key, tracks));
            _items.AddRange(tracks);
        }

        TrackList.ItemsSource = _items;
    }

    /// <summary>The folder the batch wrote to: the album folder, or the folder all the album folders share.</summary>
    private string ResultFolder()
    {
        var folders = _trackRows.Select(r => r.Folder).Where(f => !string.IsNullOrEmpty(f) && Directory.Exists(f)).Distinct().ToList();
        if (folders.Count == 0)
        {
            return null;
        }

        var common = folders[0];
        foreach (var folder in folders.Skip(1))
        {
            while (common != null && !folder.StartsWith(common.TrimEnd('/') + "/", StringComparison.Ordinal))
            {
                common = Path.GetDirectoryName(common);
            }
        }

        return string.IsNullOrEmpty(common) ? null : common;
    }

    private void ButtonOpenFolder_Click(object sender, RoutedEventArgs e)
    {
        if (ResultFolder() is { } folder)
        {
            var start = new ProcessStartInfo("xdg-open") { UseShellExecute = false };
            start.ArgumentList.Add(folder);
            try { Process.Start(start); } catch (Exception ex) { _logger.Warn(ex, "Could not open the folder"); }
        }
    }

    private void ShowTrackPanel(bool show)
    {
        if (!show)
        {
            TrackList.ItemsSource = null;
            _trackRows = [];
            _items = [];
            ButtonOpenFolder.IsVisible = false;
        }

        TrackSummary.IsVisible = false;

        _followIndex = -1;
        UpdateEmptyState();
    }

    // The first-steps hint shows in the Tracks tab until a run puts something there.
    private void UpdateEmptyState() => EmptyState.IsVisible = !_activeDownloads && _trackRows.Count == 0 && !TrackSummary.IsVisible;

    /// <summary>
    /// At the end of a batch: failed tracks move to the top, so they are never hidden below the fold of a long
    /// list (the sort is stable), and a one-line result strip shows the counts.
    /// </summary>
    private void FinishTrackList()
    {
        ShowItems(failedFirst: true);
        TrackSummary.IsVisible = true;
        ButtonOpenFolder.IsVisible = ResultFolder() != null;
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
        foreach (var row in _items)
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
        if (!_activeDownloads || _trackScroll == null || _items.Count == 0)
        {
            return;
        }

        var index = _items.FindLastIndex(r => !r.IsHeader && r.State == TrackFileState.Downloading);
        if (index < 0)
        {
            index = _items.FindIndex(r => !r.IsHeader && r.State == TrackFileState.Waiting);
        }

        var rowHeight = _trackScroll.Extent.Height / _items.Count;
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

    private void Log(string message, DownloadProgressChangedLevel level, string url = null)
    {
        _logger.Log(level.ToNLogLevel(), url == null || message.Contains(url, StringComparison.Ordinal) ? message : $"{message} [{url}]");

        if (!_userSettings.ShowVerboseLog && level is DownloadProgressChangedLevel.VerboseInfo or DownloadProgressChangedLevel.Warning)
        {
            return;
        }

        var stamp = new Avalonia.Controls.Documents.Run(DateTime.Now.ToString("HH:mm:ss") + "  ");
        var text = new Avalonia.Controls.Documents.Run(message + Environment.NewLine);
        LogText.Inlines.Add(stamp);
        LogText.Inlines.Add(text);
        _logLines.Add((stamp, text, level));
        if (level == DownloadProgressChangedLevel.Error)
        {
            _logErrors++;
            SetLogHeader();
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

    // Ctrl+Enter starts, Esc cancels a run (after the usual confirmation), Ctrl+L clears.
    private void OnShortcut(object sender, KeyEventArgs e)
    {
        var ctrl = e.KeyModifiers == KeyModifiers.Control;
        if (ctrl && e.Key == Key.Enter && ButtonStart.IsVisible && ButtonStart.IsEnabled)
        {
            ButtonStart_Click(this, e);
        }
        else if (e.KeyModifiers == KeyModifiers.None && e.Key == Key.Escape && ButtonStop.IsVisible && ButtonStop.IsEnabled)
        {
            ButtonStop_Click(this, e);
        }
        else if (ctrl && e.Key == Key.L && ButtonClear.IsEnabled)
        {
            ButtonClear_Click(this, e);
        }
        else
        {
            return;
        }

        e.Handled = true;
    }

    // Size, position and maximized state come back at the next start; a position outside every screen is ignored.
    private void RestoreWindowGeometry()
    {
        if (_userSettings.WindowWidth <= 0 || _userSettings.WindowHeight <= 0)
        {
            return;
        }

        Width = Math.Max(MinWidth, _userSettings.WindowWidth);
        Height = Math.Max(MinHeight, _userSettings.WindowHeight);
        var point = new PixelPoint(_userSettings.WindowX, _userSettings.WindowY);
        if (Screens.All.Any(s => s.WorkingArea.Contains(new PixelPoint(point.X + 40, point.Y + 20))))
        {
            WindowStartupLocation = WindowStartupLocation.Manual;
            Position = point;
        }

        if (_userSettings.WindowMaximized)
        {
            WindowState = WindowState.Maximized;
        }
    }

    private void SaveWindowGeometry()
    {
        _userSettings.WindowMaximized = WindowState == WindowState.Maximized;
        if (WindowState == WindowState.Normal) // a maximized or minimized window has no useful size to keep
        {
            _userSettings.WindowWidth = (int)Width;
            _userSettings.WindowHeight = (int)Height;
            _userSettings.WindowX = Position.X;
            _userSettings.WindowY = Position.Y;
        }
    }

    private async void OnClosing(object sender, WindowClosingEventArgs e)
    {
        SaveWindowGeometry();
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
