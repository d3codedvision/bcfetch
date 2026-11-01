using BcFetch.Bandcamp.Download;
using NLog;

namespace BcFetch.Core.Logging;

internal static class LogHelper
{
    public static LogLevel ToNLogLevel(this DownloadProgressChangedLevel level) => level switch
    {
        DownloadProgressChangedLevel.VerboseInfo => LogLevel.Debug,
        DownloadProgressChangedLevel.Warning => LogLevel.Warn,
        DownloadProgressChangedLevel.Error => LogLevel.Error,
        _ => LogLevel.Info,
    };
}
