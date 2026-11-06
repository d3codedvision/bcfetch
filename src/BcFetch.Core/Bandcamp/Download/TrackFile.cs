namespace BcFetch.Bandcamp.Download;

internal enum TrackFileState
{
    Waiting,
    Downloading,
    Done,
    Skipped,
    Failed,
}

internal sealed class TrackFile
{
    public long BytesReceived { get; set; }
    public bool Downloaded { get; set; }
    public long Size { get; }
    public string Url { get; }

    /// <summary>Display name of a track, or null for files that are not listed (cover art).</summary>
    public string Name { get; }

    /// <summary>Album title, folder of the album and the track's own label ("01 Title"); null for cover art.</summary>
    public string Album { get; init; }
    public string Folder { get; init; }
    public string Title { get; init; }

    public volatile TrackFileState State;

    /// <summary>The last error that made a try fail, for the download log.</summary>
    public volatile string Error;

    public TrackFile(string url, long bytesReceived, long size, string name = null)
    {
        Url = url;
        BytesReceived = bytesReceived;
        Size = size;
        Name = name;
    }
}
