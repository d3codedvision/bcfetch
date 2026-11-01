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

    public volatile TrackFileState State;

    public TrackFile(string url, long bytesReceived, long size, string name = null)
    {
        Url = url;
        BytesReceived = bytesReceived;
        Size = size;
        Name = name;
    }
}
