using System;
using System.IO;
using System.Text.Json;
using BcFetch.Core;
using Config.Net;

namespace BcFetch.Settings;

internal interface ISettingsService
{
    IUserSettings InitializeSettings();
    IUserSettings ResetSettings();
    IUserSettings GetUserSettings();
    string GetUserSettingsInJson();
}

public sealed class SettingsService : ISettingsService
{
    /// <summary>
    /// The absolute path to the settings file.
    /// </summary>
    private static readonly string USER_SETTINGS_FILE_PATH = Path.Combine(AppPaths.ConfigDir, "bcfetch.ini");

    private IUserSettings _userSettings;

    public IUserSettings InitializeSettings()
    {
        Directory.CreateDirectory(AppPaths.ConfigDir);
        _userSettings = new ConfigurationBuilder<IUserSettings>().UseIniFile(USER_SETTINGS_FILE_PATH).Build();

        if (string.IsNullOrEmpty(_userSettings.DownloadsPath))
        {
            // Its default value cannot be set in settings as it isn't determined by a constant function
            _userSettings.DownloadsPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyMusic), "{artist}", "{album}");
        }

        return _userSettings;
    }

    public IUserSettings ResetSettings()
    {
        File.Delete(USER_SETTINGS_FILE_PATH);
        _userSettings = InitializeSettings();

        return _userSettings;
    }

    public IUserSettings GetUserSettings()
    {
        return _userSettings;
    }

    public string GetUserSettingsInJson()
    {
        var serializedSettings = JsonSerializer.Serialize(_userSettings);
        return serializedSettings;
    }
}
