using System.ComponentModel;
using System.Globalization;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Styling;
using BcFetch.Settings;
using Res = BcFetch.App.Properties.Resources;

namespace BcFetch.App;

/// <summary>
/// Settings dialog built from a table of IUserSettings properties (editor type comes from the property type).
/// Edits go to a buffer; nothing is written until Save.
/// </summary>
internal sealed class SettingsWindow : Window
{
    // Tab title key, then rows: property, label resource key, tooltip resource key (optional).
    private static readonly (string Tab, (string Prop, string Label, string Tip)[] Rows)[] Tabs =
    [
        ("General", [
            ("CheckForUpdates", "checkBoxCheckForUpdates", null),
            ("ShowTrayIcon", "checkBoxShowTrayIcon", "checkBoxShowTrayIcon_ToolTip"),
            ("CloseToTray", "checkBoxCloseToTray", "checkBoxCloseToTray_ToolTip"),
            ("NotifyOnFinish", "checkBoxNotifyOnFinish", "checkBoxNotifyOnFinish_ToolTip"),
            ("ShowVerboseLog", "checkBoxVerboseLog", "checkBoxVerboseLog_ToolTip"),
            ("Language", "labelLanguage", null),
            ("Theme", "labelTheme", null),
        ]),
        ("Downloads", [
            ("MaxConcurrentAlbumsDownloads", "labelMaxConcurrentAlbumsDownloads", "textBoxMaxConcurrentAlbumsDownloads_ToolTip"),
            ("MaxConcurrentTracksDownloads", "labelMaxConcurrentTracksDownloads", "textBoxMaxConcurrentTracksDownloads_ToolTip"),
            ("RetrieveFilesSize", "checkBoxRetrieveFilesSize", "checkBoxRetrieveFilesSize_ToolTip"),
            ("SkipCompletedAlbums", "checkBoxSkipCompletedAlbums", "checkBoxSkipCompletedAlbums_ToolTip"),
            ("WriteDownloadLog", "checkBoxWriteDownloadLog", "checkBoxWriteDownloadLog_ToolTip"),
        ]),
        ("Tags", [
            ("FileNameFormat", "labelFileNameFormat", "textBoxFileNameFormat_ToolTip"),
            ("ModifyTags", "checkBoxModifyTags", "checkBoxModifyTags_ToolTip"),
            ("TagArtist", "labelArtist", null),
            ("TagTrackNumber", "labelTrackNumber", null),
            ("TagTrackTitle", "labelTrackTitle", null),
            ("TagAlbumTitle", "labelAlbumTitle", null),
            ("TagAlbumArtist", "labelAlbumArtist", null),
            ("TagYear", "labelYear", null),
            ("TagLyrics", "labelLyrics", null),
            ("TagComments", "labelComments", null),
        ]),
        ("Cover art", [
            ("SaveCoverArtInFolder", "checkBoxSaveCoverArtInFolder", "checkBoxSaveCoverArtInFolder_ToolTip"),
            ("CoverArtFileNameFormat", "labelCoverArtFileNameFormat", "textBoxCoverArtFileNameFormat_ToolTip"),
            ("CoverArtInFolderConvertToJpg", "checkBoxCoverArtInFolderConvertToJpg", "checkBoxCoverArtInFolderConvertToJpg_ToolTip"),
            ("CoverArtInFolderResize", "checkBoxCoverArtInFolderResize", "checkBoxCoverArtInFolderResize_ToolTip"),
            ("CoverArtInFolderMaxSize", "labelCoverArtInFolderMaxSize", null),
            ("SaveCoverArtInTags", "checkBoxSaveCoverArtInTags", "checkBoxSaveCoverArtInTags_ToolTip"),
            ("CoverArtInTagsConvertToJpg", "checkBoxCoverArtInTagsConvertToJpg", "checkBoxCoverArtInTagsConvertToJpg_ToolTip"),
            ("CoverArtInTagsResize", "checkBoxCoverArtInTagsResize", "checkBoxCoverArtInTagsResize_ToolTip"),
            ("CoverArtInTagsMaxSize", "labelCoverArtInTagsMaxSize", null),
        ]),
        ("Playlist", [
            ("CreatePlaylist", "checkBoxCreatePlaylist", "checkBoxCreatePlaylist_ToolTip"),
            ("PlaylistFormat", "labelPlaylistFormat", null),
            ("PlaylistFileNameFormat", "labelPlaylistFileNameFormat", "textBoxPlaylistFileNameFormat_ToolTip"),
            ("M3uExtended", "checkBoxM3uExtended", "checkBoxM3uExtended_ToolTip"),
        ]),
        ("Network", [
            ("Proxy", "labelProxy", null),
            ("ProxyHttpAddress", "labelHttp", null),
            ("ProxyHttpPort", "labelHttpPort", null),
        ]),
        ("Advanced", [
            ("AllowedFileSizeDifference", "labelAllowedFileSizeDifference", "textBoxAllowedFileSizeDifference_ToolTip"),
            ("DownloadMaxTries", "labelDownloadMaxTries", "textBoxDownloadMaxTries_ToolTip"),
            ("DownloadRetryCooldown", "labelDownloadRetryCooldown", "textBoxDownloadRetryCooldown_ToolTip"),
            ("DownloadRetryExponent", "labelDownloadRetryExponent", "textBoxDownloadRetryExponent_ToolTip"),
        ]),
    ];

    // Resource key of each tab title; the English tab name above stays the identifier.
    private static readonly Dictionary<string, string> TabTitleKeys = new()
    {
        ["General"] = "labelListBoxItemGeneral",
        ["Downloads"] = "labelListBoxItemDownloads",
        ["Tags"] = "labelListBoxItemTags",
        ["Cover art"] = "labelListBoxItemCoverArt",
        ["Playlist"] = "labelListBoxItemPlaylist",
        ["Network"] = "labelListBoxItemNetwork",
        ["Advanced"] = "labelListBoxItemAdvanced",
    };

    // A row is enabled only when all these bool properties are true (Proxy is handled separately).
    private const double LabelWidth = 250;
    private readonly Dictionary<string, TextBlock> _messages = new();

    private static readonly Dictionary<string, string[]> EnabledWhen = new()
    {
        ["CoverArtFileNameFormat"] = ["SaveCoverArtInFolder"],
        ["CoverArtInFolderConvertToJpg"] = ["SaveCoverArtInFolder"],
        ["CoverArtInFolderResize"] = ["SaveCoverArtInFolder"],
        ["CoverArtInFolderMaxSize"] = ["SaveCoverArtInFolder", "CoverArtInFolderResize"],
        ["CoverArtInTagsConvertToJpg"] = ["SaveCoverArtInTags"],
        ["CoverArtInTagsResize"] = ["SaveCoverArtInTags"],
        ["CoverArtInTagsMaxSize"] = ["SaveCoverArtInTags", "CoverArtInTagsResize"],
        ["CloseToTray"] = ["ShowTrayIcon"],
        ["PlaylistFormat"] = ["CreatePlaylist"],
        ["PlaylistFileNameFormat"] = ["CreatePlaylist"],
        ["M3uExtended"] = ["CreatePlaylist"],
    };

    private static readonly string[] TagProps = ["TagArtist", "TagTrackNumber", "TagTrackTitle", "TagAlbumTitle", "TagAlbumArtist", "TagYear", "TagLyrics", "TagComments"];

    private readonly ISettingsService _settingsService;
    private readonly Dictionary<string, object> _values = new();
    private readonly Dictionary<string, Control> _controls = new();
    private readonly HashSet<string> _invalid = new();
    private readonly Button _saveButton = new() { Content = T("buttonSave"), MinWidth = 90, IsDefault = true, Classes = { "primary" } };
    private bool _loading;

    public SettingsWindow(ISettingsService settingsService, bool activeDownloads)
    {
        _settingsService = settingsService;
        Title = T("windowSettings_Title");
        Width = 720;
        Height = 540;
        MinWidth = 720; // the label and field columns need this width
        MinHeight = 440;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;

        var tabs = new TabControl { TabStripPlacement = Dock.Left };
        foreach (var (tab, rows) in Tabs)
        {
            var panel = new StackPanel { Margin = new(24, 12, 24, 12), Spacing = 12 };
            foreach (var (prop, label, tip) in rows)
            {
                panel.Children.Add(BuildRow(prop, label, tip));
            }

            // Only General can change while downloading.
            tabs.Items.Add(new TabItem { Header = T(TabTitleKeys[tab]) ?? tab, Content = new ScrollViewer { Content = panel }, IsEnabled = !activeDownloads || tab == "General" });
        }

        var reset = new Button { Content = T("labelButtonResetSettings") };
        reset.SetValue(ToolTip.TipProperty, T("buttonResetSettings_ToolTip"));
        reset.Click += async (_, _) =>
        {
            if (await Dialogs.ConfirmAsync(this, T("messageBoxResetSettingsText"), T("messageBoxResetSettingsButtonOk"), T("messageBoxButtonCancel")))
            {
                // Keep the settings that live on the main window.
                var s = _settingsService.GetUserSettings();
                var (path, discography) = (s.DownloadsPath, s.DownloadArtistDiscography);
                s = _settingsService.ResetSettings();
                s.DownloadsPath = path;
                s.DownloadArtistDiscography = discography;
                Load();
            }
        };
        var cancel = new Button { Content = T("buttonCancel"), MinWidth = 90, IsCancel = true };
        cancel.Click += (_, _) => Close();
        _saveButton.Click += (_, _) => { Save(); Close(); };

        var footer = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, HorizontalAlignment = HorizontalAlignment.Right, Margin = new(20, 8, 20, 18), Children = { reset, _saveButton, cancel } };
        var root = new DockPanel();
        DockPanel.SetDock(footer, Dock.Bottom);
        root.Children.Add(footer);
        if (activeDownloads)
        {
            var info = new TextBlock { Text = T("labelSettingsInfo"), Margin = new(24, 6) };
            info.Bind(TextBlock.ForegroundProperty, this.GetResourceObservable("BcMuted"));
            DockPanel.SetDock(info, Dock.Bottom);
            root.Children.Add(info);
        }
        root.Children.Add(tabs);
        Content = root;

        Load();
    }

    private Control BuildRow(string prop, string labelKey, string tipKey)
    {
        var type = typeof(IUserSettings).GetProperty(prop)!.PropertyType;
        Control editor;
        if (type == typeof(bool))
        {
            var box = new CheckBox { Content = T(labelKey) };
            box.IsCheckedChanged += (_, _) => Set(prop, box.IsChecked == true);
            editor = box;
        }
        else if (type.IsEnum)
        {
            var combo = new ComboBox { Width = 200, ItemsSource = EnumItems(prop, type) };
            combo.SelectionChanged += (_, _) => { if (combo.SelectedItem is EnumItem item) Set(prop, item.Value); };
            editor = combo;
        }
        else
        {
            var box = new TextBox { Width = 200, HorizontalAlignment = HorizontalAlignment.Left };
            box.TextChanged += (_, _) => OnText(prop, type, box);
            editor = box;
        }

        _controls[prop] = editor;
        AutomationProperties.SetName(editor, T(labelKey));
        if (tipKey != null)
        {
            ToolTip.SetTip(editor, T(tipKey));
        }

        if (type == typeof(bool))
        {
            return editor;
        }

        // Labels wrap instead of being cut off: translated labels are often much longer than the English ones.
        var label = new TextBlock { Text = T(labelKey), Width = LabelWidth, TextWrapping = TextWrapping.Wrap, VerticalAlignment = VerticalAlignment.Center };
        var message = new TextBlock { FontSize = 12, TextWrapping = TextWrapping.Wrap, IsVisible = false, Margin = new Thickness(LabelWidth + 10, 0, 0, 0) };
        message.Bind(TextBlock.ForegroundProperty, this.GetResourceObservable("BcDanger"));
        _messages[prop] = message;
        return new StackPanel
        {
            Spacing = 4,
            Children =
            {
                new StackPanel { Orientation = Orientation.Horizontal, Spacing = 10, Children = { label, editor } },
                message,
            },
        };
    }

    private sealed record EnumItem(object Value, string Text)
    {
        public override string ToString() => Text;
    }

    private static List<EnumItem> EnumItems(string prop, Type type)
    {
        return Enum.GetValues(type).Cast<object>().Select(v =>
        {
            var text = prop == "Proxy" ? T($"radioButton{v}Proxy") : T($"{type.Name}_{v}");
            text ??= type.GetField(v.ToString()!)!.GetCustomAttributes(typeof(DescriptionAttribute), false).Cast<DescriptionAttribute>().FirstOrDefault()?.Description ?? v.ToString();
            return new EnumItem(v, text);
        }).ToList();
    }

    private void OnText(string prop, Type type, TextBox box)
    {
        if (_loading)
        {
            return;
        }

        var text = box.Text ?? "";
        object value = null;
        var ok = type == typeof(string) ? (value = text) != null
               : type == typeof(int) ? int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var i) && (value = i) != null
               : double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var d) && (value = d) != null;
        if (ok && prop == "FileNameFormat" && !text.EndsWith(".mp3", StringComparison.Ordinal))
        {
            ok = false; // Same rule as the WPF version: must end with ".mp3".
        }

        // A bad value is marked by a danger-coloured border and a sentence under the field, not by colour alone
        if (ok)
        {
            box.ClearValue(TemplatedControl.BorderBrushProperty);
            _messages[prop].IsVisible = false;
            _invalid.Remove(prop);
            Set(prop, value);
        }
        else
        {
            box.BorderBrush = this.Token("BcDanger");
            _messages[prop].Text = T(prop == "FileNameFormat" ? "settingsInvalidFileName" : type == typeof(int) ? "settingsInvalidInteger" : "settingsInvalidNumber");
            _messages[prop].IsVisible = true;
            _invalid.Add(prop);
        }

        _saveButton.IsEnabled = _invalid.Count == 0;
    }

    private void Set(string prop, object value)
    {
        if (_loading)
        {
            return;
        }

        _values[prop] = value;
        Refresh();
    }

    private void Load()
    {
        _loading = true;
        var settings = _settingsService.GetUserSettings();
        foreach (var (prop, control) in _controls)
        {
            var value = typeof(IUserSettings).GetProperty(prop)!.GetValue(settings);
            _values[prop] = value;
            switch (control)
            {
                case CheckBox c: c.IsChecked = (bool)value; break;
                case ComboBox c: c.SelectedItem = ((List<EnumItem>)c.ItemsSource).First(i => i.Value.Equals(value)); break;
                case TextBox c: c.Text = Convert.ToString(value, CultureInfo.InvariantCulture); c.ClearValue(TemplatedControl.BorderBrushProperty); if (_messages.TryGetValue(prop, out var m)) m.IsVisible = false; break;
            }
        }

        _invalid.Clear();
        _saveButton.IsEnabled = true;
        _loading = false;
        Refresh();
    }

    private void Refresh()
    {
        foreach (var (prop, control) in _controls)
        {
            var enabled = !EnabledWhen.TryGetValue(prop, out var parents) || parents.All(p => (bool)_values[p]);
            if (TagProps.Contains(prop))
            {
                enabled = (bool)_values["ModifyTags"];
            }
            else if (prop is "ProxyHttpAddress" or "ProxyHttpPort")
            {
                enabled = (ProxyType)_values["Proxy"] == ProxyType.Manual;
            }

            control.IsEnabled = enabled;
        }
    }

    private void Save()
    {
        var settings = _settingsService.GetUserSettings();
        foreach (var (prop, value) in _values)
        {
            typeof(IUserSettings).GetProperty(prop)!.SetValue(settings, value);
        }

        if (Application.Current is { } app)
        {
            app.RequestedThemeVariant = settings.Theme == Skin.Dark ? ThemeVariant.Dark : ThemeVariant.Light;
        }

        App.ApplyLanguage(settings.Language);

        NLog.LogManager.GetCurrentClassLogger().Info($"Settings saved: {_settingsService.GetUserSettingsInJson()}");
    }

    /// <summary>Resource lookup without mnemonic underscores; null when the key does not exist.</summary>
    private static string T(string key) => Res.ResourceManager.GetString(key, Res.Culture)?.Replace("_", "");
}
