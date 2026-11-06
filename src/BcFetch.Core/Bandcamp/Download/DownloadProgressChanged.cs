using System;

namespace BcFetch.Bandcamp.Download;

internal delegate void DownloadProgressChangedEventHandler(object sender, DownloadProgressChangedArgs eventArgs);

internal sealed class DownloadProgressChangedArgs : EventArgs
{
    public DownloadProgressChangedLevel Level { get; private set; }
    public string Message { get; private set; }

    /// <summary>The download link the message is about; written to the log file with the message, not shown in the window.</summary>
    public string Url { get; private set; }

    public DownloadProgressChangedArgs(string message, DownloadProgressChangedLevel level, string url = null)
    {
        Message = message;
        Level = level;
        Url = url;
    }

    /// <summary>The message with its link, as the log file gets it.</summary>
    public string ForFile => Url == null || Message.Contains(Url, StringComparison.Ordinal) ? Message : $"{Message} [{Url}]";
}

internal enum DownloadProgressChangedLevel
{
    VerboseInfo,
    Info,
    IntermediateSuccess,
    Success,
    Warning,
    Error,
}
