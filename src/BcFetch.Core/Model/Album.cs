using System;
using System.Collections.Generic;
using BcFetch.Core.DependencyInjection;
using BcFetch.IO;
using BcFetch.Settings;

namespace BcFetch.Model;

internal sealed class Album
{
    private readonly IUserSettings _userSettings;

    /// <summary>
    /// The album artist.
    /// </summary>
    public string Artist { get; set; }

    /// <summary>
    /// The local path (full path with file name) where the artwork file should be saved.
    /// </summary>
    public string ArtworkPath { get; private set; }

    /// <summary>
    /// The URL where the artwork should be downloaded from.
    /// </summary>
    public string ArtworkUrl { get; set; }

    /// <summary>The Bandcamp page the album was read from.</summary>
    public string PageUrl { get; set; }

    /// <summary>
    /// True if the album has an artwork; false otherwise.
    /// </summary>
    public bool HasArtwork => ArtworkUrl != null;

    /// <summary>
    /// The local path (full path) to the folder where the album should be saved.
    /// </summary>
    public string Path { get; }

    /// <summary>
    /// The local path (full path with file name) where the playlist file should be saved.
    /// </summary>
    public string PlaylistPath { get; private set; }

    /// <summary>
    /// The release date of the album.
    /// </summary>
    public DateTime ReleaseDate { get; set; }

    /// <summary>
    /// The album title.
    /// </summary>
    public string Title { get; set; }

    /// <summary>
    /// The list of tracks contained in the album.
    /// </summary>
    public List<Track> Tracks { get; set; }

    /// <summary>
    /// Initializes a new Album.
    /// </summary>
    public Album(string artist, string artworkUrl, DateTime releaseDate, string title)
    {
        _userSettings = DependencyInjectionHelper.GetService<ISettingsService>().GetUserSettings();

        Artist = artist;
        ArtworkUrl = artworkUrl;
        ReleaseDate = releaseDate;
        Title = title;
        // Must be done after other properties are filled!
        Path = ParseFolderPath();
        // Must be done after Path is set!
        PlaylistPath = ParsePlaylistPath();
        SetArtworkPath();
    }

    /// <summary>
    /// Returns the file extension to be used for the playlist, depending of the type of playlist defined in UserSettings.
    /// </summary>
    private string GetPlaylistFileExtension()
    {
        return _userSettings.PlaylistFormat switch
        {
            PlaylistFormat.m3u => ".m3u",
            PlaylistFormat.pls => ".pls",
            PlaylistFormat.wpl => ".wpl",
            PlaylistFormat.zpl => ".zpl",
            _ => throw new ArgumentOutOfRangeException(),
        };
    }

    /// <summary>
    /// Returns the file name to be used for the cover art of the specified album from the file name format saved in
    /// the UserSettings, by replacing the placeholders strings with their corresponding values. The returned file
    /// name does NOT contain the extension.
    /// </summary>
    private string ParseCoverArtFileName()
    {
        var fileName = _userSettings.CoverArtFileNameFormat
            .Replace("{year}", ReleaseDate.Year.ToString())
            .Replace("{month}", ReleaseDate.Month.ToString("00"))
            .Replace("{day}", ReleaseDate.Day.ToString("00"))
            .Replace("{album}", Title)
            .Replace("{artist}", Artist);
        return fileName.ToAllowedFileName();
    }

    /// <summary>
    /// Returns the folder path from the specified path format, by replacing the placeholders strings with their
    /// corresponding values. Each component is shortened to the file system limit of 255 bytes.
    /// </summary>
    private string ParseFolderPath()
    {
        var path = _userSettings.DownloadsPath;
        path = path.Replace("{year}", ReleaseDate.Year.ToString().ToAllowedFileName());
        path = path.Replace("{month}", ReleaseDate.Month.ToString("00").ToAllowedFileName());
        path = path.Replace("{day}", ReleaseDate.Day.ToString("00").ToAllowedFileName());
        path = path.Replace("{artist}", Artist.ToAllowedFileName());
        path = path.Replace("{album}", Title.ToAllowedFileName());

        return PathLimits.FitPath(path);
    }

    /// <summary>
    /// Returns the file name to be used for the playlist file of the specified album from the file name format saved
    /// in the UserSettings, by replacing the placeholders strings with their corresponding values.
    /// </summary>
    private string ParsePlaylistFileName()
    {
        var fileName = _userSettings.PlaylistFileNameFormat
            .Replace("{year}", ReleaseDate.Year.ToString())
            .Replace("{month}", ReleaseDate.Month.ToString("00"))
            .Replace("{day}", ReleaseDate.Day.ToString("00"))
            .Replace("{album}", Title)
            .Replace("{artist}", Artist);
        return fileName.ToAllowedFileName();
    }

    /// <summary>
    /// Returns the path to be used for the playlist file from the file name format saved in the UserSettings, by
    /// replacing the placeholders strings with their corresponding values. The file name is shortened to the file system limit.
    /// </summary>
    private string ParsePlaylistPath()
    {
        var fileExt = GetPlaylistFileExtension();

        return Path + System.IO.Path.DirectorySeparatorChar + PathLimits.FitFileName(ParsePlaylistFileName() + fileExt);
    }

    /// <summary>
    /// Sets the ArtworkPath properties.
    /// </summary>
    private void SetArtworkPath()
    {
        if (HasArtwork)
        {
            var artworkFileExt = System.IO.Path.GetExtension(ArtworkUrl);

            ArtworkPath = Path + System.IO.Path.DirectorySeparatorChar + PathLimits.FitFileName(ParseCoverArtFileName() + artworkFileExt);
        }
    }
}
