using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using BcFetch.Audio;
using BcFetch.Core;
using BcFetch.Helpers;
using BcFetch.IO;
using BcFetch.Model;
using BcFetch.Settings;
using Downloader;
using NLog;

namespace BcFetch.Bandcamp.Download;

internal interface IDownloadManager
{
    Task InitializeAsync(string inputUrls, CancellationToken cancellationToken);
    Task StartDownloadsAsync(CancellationToken cancellationToken);
    long GetTotalBytesReceived();
    long GetTotalBytesToDownload();
    int GetTotalFilesCountReceived();
    int GetTotalFilesCountToDownload();
    int GetFailedTracksCount();
    IReadOnlyCollection<TrackFile> GetTrackFiles();
    event DownloadProgressChangedEventHandler DownloadProgressChanged;
}

internal sealed class DownloadManager : IDownloadManager
{
    private readonly IPlaylistCreator _playlistCreator;
    private readonly ITagService _tagService;
    private readonly ITrackFileService _trackFileService;
    private readonly IAlbumUrlRetriever _albumUrlRetriever;
    private readonly IAlbumInfoRetriever _albumInfoRetriever;
    private readonly IResilienceService _resilienceService;
    private readonly IImageService _imageService;
    private readonly IUserSettings _userSettings;
    private readonly Logger _logger = LogManager.GetCurrentClassLogger();
    private bool _isInitialized;
    private int _failedTracksCount;
    private IReadOnlyCollection<TrackFile> _downloadingFiles;
    private IReadOnlyCollection<Album> _albums;

    public event DownloadProgressChangedEventHandler DownloadProgressChanged;

    public DownloadManager(IPlaylistCreator playlistCreator, ITagService tagService, IResilienceService resilienceService, ITrackFileService trackFileService, IAlbumUrlRetriever albumUrlRetriever, IAlbumInfoRetriever albumInfoRetriever, IImageService imageService, ISettingsService settingsService)
    {
        _playlistCreator = playlistCreator;
        _tagService = tagService;
        _resilienceService = resilienceService;
        _trackFileService = trackFileService;
        _albumUrlRetriever = albumUrlRetriever;
        _albumInfoRetriever = albumInfoRetriever;
        _imageService = imageService;
        _userSettings = settingsService.GetUserSettings();

        _albumUrlRetriever.DownloadProgressChanged += OnDownloadProgressChanged;
        _albumInfoRetriever.DownloadProgressChanged += OnDownloadProgressChanged;
        _trackFileService.DownloadProgressChanged += OnDownloadProgressChanged;
    }

    private void OnDownloadProgressChanged(object sender, DownloadProgressChangedArgs eventArgs)
    {
        DownloadProgressChanged?.Invoke(sender, eventArgs);
    }

    public async Task InitializeAsync(string inputUrls, CancellationToken cancellationToken)
    {
        var albumsUrls = await _albumUrlRetriever.RetrieveAlbumsUrlsAsync(inputUrls, _userSettings.DownloadArtistDiscography, cancellationToken).ConfigureAwait(false);
        var albums = await _albumInfoRetriever.GetAlbumsAsync(albumsUrls, cancellationToken).ConfigureAwait(false);
        _albums = _userSettings.SkipCompletedAlbums ? albums.Where(a => !SkipIfComplete(a)).ToList() : albums;
        _downloadingFiles = await _trackFileService.GetFilesToDownloadAsync(_albums, cancellationToken).ConfigureAwait(false);

        _failedTracksCount = 0;
        _isInitialized = true;
    }

    public async Task StartDownloadsAsync(CancellationToken cancellationToken)
    {
        try
        {
            ThrowIfNotInitialized();

            var parallelOptions = new ParallelOptions
            {
                CancellationToken = cancellationToken,
                MaxDegreeOfParallelism = _userSettings.MaxConcurrentAlbumsDownloads,
            };

            await Parallel.ForEachAsync(
                _albums,
                parallelOptions,
                async (album, ct) =>
                {
                    try
                    {
                        await DownloadAlbumAsync(album, ct).ConfigureAwait(false);
                    }
                    finally
                    {
                        // Also after a cancellation, so the log shows what was finished
                        if (_userSettings.WriteDownloadLog)
                        {
                            AlbumLog.Append(album.Path, DescribeAlbumRun(album));
                        }
                    }
                }).ConfigureAwait(false);
        }
        finally
        {
            _isInitialized = false;
        }
    }

    public long GetTotalBytesReceived()
    {
        return _downloadingFiles.Sum(f => f.BytesReceived);
    }

    public long GetTotalBytesToDownload()
    {
        return _downloadingFiles.Sum(f => f.Size);
    }

    public int GetTotalFilesCountToDownload()
    {
        return _downloadingFiles.Count;
    }

    public IReadOnlyCollection<TrackFile> GetTrackFiles()
    {
        return _downloadingFiles.Where(f => f.Name != null).ToList();
    }

    public int GetFailedTracksCount()
    {
        return _failedTracksCount;
    }

    public int GetTotalFilesCountReceived()
    {
        return _downloadingFiles.Count(f => f.Downloaded);
    }

    private bool SkipIfComplete(Album album)
    {
        if (!AlbumMarker.IsComplete(album.Path, album.Tracks.Select(t => t.Path)))
        {
            return false;
        }

        DownloadProgressChanged?.Invoke(this, new DownloadProgressChangedArgs($"Skipping album \"{album.Title}\": already downloaded", DownloadProgressChangedLevel.Info, album.PageUrl));
        if (_userSettings.WriteDownloadLog)
        {
            AlbumLog.Append(album.Path, [$"bcfetch {Constants.APP_VERSION_FORMATTED} | {album.Artist} - {album.Title}", $"  page: {album.PageUrl}", "  album skipped: already downloaded"]);
        }

        return true;
    }

    /// <summary>The log lines for one album after a run: what happened to each track and a one-line result.</summary>
    private IEnumerable<string> DescribeAlbumRun(Album album)
    {
        yield return $"bcfetch {Constants.APP_VERSION_FORMATTED} | {album.Artist} - {album.Title}";
        yield return $"  page: {album.PageUrl}";

        int downloaded = 0, skipped = 0, failed = 0, unfinished = 0;
        foreach (var track in album.Tracks)
        {
            var file = _downloadingFiles.First(f => f.Url == track.Mp3Url);
            var name = Path.GetFileName(track.Path);
            switch (file.State)
            {
                case TrackFileState.Done:
                    downloaded++;
                    var size = File.Exists(track.Path) ? $" ({new FileInfo(track.Path).Length / (1024.0 * 1024):0.0} MB)" : "";
                    yield return $"  downloaded  {name}{size}  <- {track.Mp3Url}";
                    break;
                case TrackFileState.Skipped:
                    skipped++;
                    yield return $"  skipped     {name} (already on disk)  <- {track.Mp3Url}";
                    break;
                case TrackFileState.Failed:
                    failed++;
                    yield return $"  FAILED      {name}{(file.Error == null ? "" : ": " + file.Error)}  <- {track.Mp3Url}";
                    break;
                default:
                    unfinished++;
                    yield return $"  unfinished  {name} (cancelled or not started)  <- {track.Mp3Url}";
                    break;
            }
        }

        yield return $"  result: {downloaded} downloaded, {skipped} skipped, {failed} failed" + (unfinished > 0 ? $", {unfinished} unfinished" : "");
    }

    private void ThrowIfNotInitialized()
    {
        if (!_isInitialized)
        {
            throw new InvalidOperationException($"{nameof(DownloadManager)} has not been initialized.");
        }
    }

    private async Task DownloadAlbumAsync(Album album, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        // Create a directory to save track files
        try
        {
            Directory.CreateDirectory(album.Path);
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Could not create album directory");
            DownloadProgressChanged?.Invoke(this, new DownloadProgressChangedArgs("An error occurred when creating the album folder. Make sure you have the rights to write files in the folder you chose", DownloadProgressChangedLevel.Error));
            return;
        }

        byte[] inTagsArtwork = null;

        // Download artwork
        if ((_userSettings.SaveCoverArtInTags || _userSettings.SaveCoverArtInFolder) && album.HasArtwork)
        {
            var artwork = await DownloadCoverArtAsync(album, cancellationToken).ConfigureAwait(false);

            // Save artwork to folder
            if (_userSettings.SaveCoverArtInFolder && artwork != null)
            {
                await SaveCoverArtToFolder(album, artwork, cancellationToken).ConfigureAwait(false);
            }

            // Prepare artwork for tags
            if (_userSettings.SaveCoverArtInTags && artwork != null)
            {
                inTagsArtwork = await PrepareCoverArtForTags(artwork, cancellationToken).ConfigureAwait(false);
            }
        }

        // Download & tag tracks
        var downloadedTracksCount = 0;

        var parallelOptions = new ParallelOptions
        {
            CancellationToken = cancellationToken,
            MaxDegreeOfParallelism = _userSettings.MaxConcurrentTracksDownloads,
        };

        await Parallel.ForEachAsync(
            album.Tracks,
            parallelOptions,
            async (track, ct) =>
            {
                var trackDownloaded = await DownloadAndTagTrackAsync(album, track, inTagsArtwork, ct).ConfigureAwait(false);
                if (trackDownloaded)
                {
                    Interlocked.Increment(ref downloadedTracksCount);
                }
            }).ConfigureAwait(false);

        // Create playlist file
        if (_userSettings.CreatePlaylist)
        {
            await _playlistCreator.SavePlaylistToFileAsync(album, cancellationToken).ConfigureAwait(false);
            DownloadProgressChanged?.Invoke(this, new DownloadProgressChangedArgs($"Saved playlist for album \"{album.Title}\"", DownloadProgressChangedLevel.IntermediateSuccess, album.PageUrl));
        }

        var tracksOnDisk = album.Tracks.Count(t => _downloadingFiles.First(f => f.Url == t.Mp3Url).State is TrackFileState.Done or TrackFileState.Skipped);
        if (album.Tracks.Count == tracksOnDisk)
        {
            AlbumMarker.Mark(album.Path);
            DownloadProgressChanged?.Invoke(this, new DownloadProgressChangedArgs($"Successfully downloaded album \"{album.Title}\"", DownloadProgressChangedLevel.Success, album.PageUrl));
        }
        else
        {
            DownloadProgressChanged?.Invoke(this, new DownloadProgressChangedArgs($"Finished downloading album \"{album.Title}\". Some tracks were not downloaded", DownloadProgressChangedLevel.Success, album.PageUrl));
        }
    }

    private async Task SaveCoverArtToFolder(Album album, byte[] artwork, CancellationToken cancellationToken)
    {
        var inFolderArtwork = artwork;
        if (_userSettings.CoverArtInFolderResize)
        {
            inFolderArtwork = await _imageService.ResizeImage(inFolderArtwork, _userSettings.CoverArtInFolderMaxSize, _userSettings.CoverArtInFolderMaxSize, cancellationToken).ConfigureAwait(false);
        }

        if (_userSettings.CoverArtInFolderConvertToJpg)
        {
            inFolderArtwork = await _imageService.ConvertToJpegAsync(inFolderArtwork, cancellationToken).ConfigureAwait(false);
        }

        await File.WriteAllBytesAsync(album.ArtworkPath, inFolderArtwork, cancellationToken).ConfigureAwait(false);
    }

    private async Task<byte[]> PrepareCoverArtForTags(byte[] artwork, CancellationToken cancellationToken)
    {
        var inTagsArtwork = artwork;
        if (_userSettings.CoverArtInTagsResize)
        {
            inTagsArtwork = await _imageService.ResizeImage(inTagsArtwork, _userSettings.CoverArtInTagsMaxSize, _userSettings.CoverArtInTagsMaxSize, cancellationToken).ConfigureAwait(false);
        }

        if (_userSettings.CoverArtInTagsConvertToJpg)
        {
            inTagsArtwork = await _imageService.ConvertToJpegAsync(inTagsArtwork, cancellationToken).ConfigureAwait(false);
        }

        return inTagsArtwork;
    }

    /// <summary>
    /// Downloads and tags a track. Returns true if the track has been correctly downloaded; false otherwise.
    /// </summary>
    /// <param name="album">The album of the track to download.</param>
    /// <param name="track">The track to download.</param>
    /// <param name="artwork">The cover art.</param>
    /// <param name="cancellationToken">The token to monitor for cancellation requests.</param>
    private async Task<bool> DownloadAndTagTrackAsync(Album album, Track track, byte[] artwork, CancellationToken cancellationToken)
    {
        var tries = 0;
        var trackDownloaded = false;

        TrackFile currentFile;
        try
        {
            currentFile = _downloadingFiles.First(f => f.Url == track.Mp3Url);
        }
        catch (Exception ex)
        {
            _logger.Error(ex);
            throw;
        }

        if (File.Exists(track.Path))
        {
            var length = new FileInfo(track.Path).Length;
            if (currentFile.Size > length - currentFile.Size * _userSettings.AllowedFileSizeDifference &&
                currentFile.Size < length + currentFile.Size * _userSettings.AllowedFileSizeDifference)
            {
                DownloadProgressChanged?.Invoke(this, new DownloadProgressChangedArgs($"Track already exists within allowed file size range: track \"{Path.GetFileName(track.Path)}\" from album \"{album.Title}\" - Skipping download!", DownloadProgressChangedLevel.IntermediateSuccess, track.Mp3Url));
                currentFile.State = TrackFileState.Skipped;
                return false;
            }
        }

        do
        {
            cancellationToken.ThrowIfCancellationRequested();

            currentFile.State = TrackFileState.Downloading;
            var downloadService = new DownloadService();

            // Update progress bar when downloading
            downloadService.DownloadProgressChanged += (_, args) =>
            {
                currentFile.BytesReceived = args.ReceivedBytesSize;
            };

            // Start download
            try
            {
                if (track.Path == null)
                {
                    throw new InvalidOperationException("Track path is null");
                }

                DownloadProgressChanged?.Invoke(this, new DownloadProgressChangedArgs($"Downloading track \"{track.Title}\" from url: {track.Mp3Url}", DownloadProgressChangedLevel.VerboseInfo));
                await downloadService.DownloadFileTaskAsync(track.Mp3Url, track.Path, cancellationToken).ConfigureAwait(false);
                cancellationToken.ThrowIfCancellationRequested(); // See https://github.com/bezzad/Downloader/issues/203

                // The library does not throw for every HTTP error (a 500 leaves a half-written ".download" file), so only
                // a file that really exists counts. Without this a failed track was reported as downloaded.
                if (!File.Exists(track.Path))
                {
                    throw new IOException("The server did not deliver the file");
                }

                trackDownloaded = true;
                DownloadProgressChanged?.Invoke(this, new DownloadProgressChangedArgs($"Downloaded track \"{track.Title}\" from url: {track.Mp3Url}", DownloadProgressChangedLevel.VerboseInfo));
            }
            catch (Exception ex) when (!cancellationToken.IsCancellationRequested)
            {
                // Any network or IO failure (429, timeout, dropped connection...) counts as a failed try; cancellation still propagates
                _logger.Error(ex);
                currentFile.Error = ex.Message;

                if (tries + 1 < _userSettings.DownloadMaxTries)
                {
                    DownloadProgressChanged?.Invoke(this, new DownloadProgressChangedArgs($"Unable to download track \"{Path.GetFileName(track.Path)}\" from album \"{album.Title}\". Try {tries + 1} of {_userSettings.DownloadMaxTries}", DownloadProgressChangedLevel.Warning, track.Mp3Url));
                }
                else
                {
                    DownloadProgressChanged?.Invoke(this, new DownloadProgressChangedArgs($"Unable to download track \"{Path.GetFileName(track.Path)}\" from album \"{album.Title}\". Hit max retries of {_userSettings.DownloadMaxTries}", DownloadProgressChangedLevel.Error, track.Mp3Url));
                }
            }

            if (trackDownloaded)
            {
                if (_userSettings.ModifyTags ||
                    (_userSettings.SaveCoverArtInTags && artwork != null))
                {
                    try
                    {
                        _tagService.SaveTagsInTrack(track, album, artwork, cancellationToken);
                        DownloadProgressChanged?.Invoke(this, new DownloadProgressChangedArgs($"Tags saved for track \"{Path.GetFileName(track.Path)}\" from album \"{album.Title}\"", DownloadProgressChangedLevel.VerboseInfo, track.Mp3Url));
                    }
                    catch (Exception ex) when (!cancellationToken.IsCancellationRequested)
                    {
                        // A corrupt download counts as a failed try instead of aborting every album
                        _logger.Error(ex);
                        currentFile.Error = "tags: " + ex.Message;
                        trackDownloaded = false;
                        DownloadProgressChanged?.Invoke(this, new DownloadProgressChangedArgs($"Unable to save tags for track \"{Path.GetFileName(track.Path)}\" from album \"{album.Title}\": {ex.Message}", DownloadProgressChangedLevel.Warning, track.Mp3Url));
                    }
                }

                if (trackDownloaded)
                {
                    // Note the file as downloaded
                    currentFile.Downloaded = true;
                    currentFile.State = TrackFileState.Done;
                    DownloadProgressChanged?.Invoke(this, new DownloadProgressChangedArgs($"Downloaded track \"{Path.GetFileName(track.Path)}\" from album \"{album.Title}\"", DownloadProgressChangedLevel.IntermediateSuccess, track.Mp3Url));
                }
            }

            tries++;
            if (!trackDownloaded && tries < _userSettings.DownloadMaxTries)
            {
                await _resilienceService.WaitForCooldownAsync(tries, cancellationToken).ConfigureAwait(false);
            }
        } while (!trackDownloaded && tries < _userSettings.DownloadMaxTries);

        if (!trackDownloaded)
        {
            currentFile.State = TrackFileState.Failed;
            Interlocked.Increment(ref _failedTracksCount);
        }

        return trackDownloaded;
    }

    /// <summary>
    /// Downloads and returns the cover art of the specified album.
    /// Returns null if the cover art could not be downloaded in the max tries attempts.
    /// </summary>
    /// <param name="album">The album.</param>
    /// <param name="cancellationToken">The token to monitor for cancellation requests.</param>
    private async Task<byte[]> DownloadCoverArtAsync(Album album, CancellationToken cancellationToken)
    {
        byte[] artwork = null;
        var tries = 0;
        var artworkDownloaded = false;

        TrackFile currentFile;
        try
        {
            currentFile = _downloadingFiles.First(f => f.Url == album.ArtworkUrl);
        }
        catch (Exception ex)
        {
            _logger.Error(ex);
            throw;
        }

        do
        {
            cancellationToken.ThrowIfCancellationRequested();

            var downloadService = new DownloadService();

            // Update progress bar when downloading
            downloadService.DownloadProgressChanged += (_, args) =>
            {
                currentFile.BytesReceived = args.ReceivedBytesSize;
            };

            // Start download
            DownloadProgressChanged?.Invoke(this, new DownloadProgressChangedArgs($"Downloading artwork from url: {album.ArtworkUrl}", DownloadProgressChangedLevel.VerboseInfo));

            try
            {
                await using var artworkStream = await downloadService.DownloadFileTaskAsync(album.ArtworkUrl, cancellationToken).ConfigureAwait(false);
                cancellationToken.ThrowIfCancellationRequested(); // See https://github.com/bezzad/Downloader/issues/203

                artwork = await artworkStream.ToArrayAsync(cancellationToken).ConfigureAwait(false);
                artworkDownloaded = true;
            }
            catch (Exception ex) when (!cancellationToken.IsCancellationRequested)
            {
                // Any network or IO failure (429, timeout, dropped connection...) counts as a failed try; cancellation still propagates
                _logger.Error(ex);

                if (tries < _userSettings.DownloadMaxTries)
                {
                    DownloadProgressChanged?.Invoke(this, new DownloadProgressChangedArgs($"Unable to download artwork for album \"{album.Title}\". Try {tries + 1} of {_userSettings.DownloadMaxTries}", DownloadProgressChangedLevel.Warning, album.ArtworkUrl));
                }
                else
                {
                    DownloadProgressChanged?.Invoke(this, new DownloadProgressChangedArgs($"Unable to download artwork for album \"{album.Title}\". Hit max retries of {_userSettings.DownloadMaxTries}", DownloadProgressChangedLevel.Error, album.ArtworkUrl));
                }
            }

            if (artworkDownloaded)
            {
                currentFile.Downloaded = true;
                DownloadProgressChanged?.Invoke(this, new DownloadProgressChangedArgs($"Downloaded artwork for album \"{album.Title}\"", DownloadProgressChangedLevel.IntermediateSuccess, album.ArtworkUrl));
            }

            tries++;
            if (!artworkDownloaded && tries < _userSettings.DownloadMaxTries)
            {
                await _resilienceService.WaitForCooldownAsync(tries, cancellationToken).ConfigureAwait(false);
            }
        } while (!artworkDownloaded && tries < _userSettings.DownloadMaxTries);

        return artwork;
    }
}
