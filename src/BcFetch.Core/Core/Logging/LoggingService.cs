using System;
using System.IO;
using BcFetch.Core;
using NLog;
using NLog.Config;
using NLog.Targets;

namespace BcFetch.Core.Logging;

internal interface ILoggingService
{
    void InitializeLogger();
}

public sealed class LoggingService : ILoggingService
{
    /// <summary>
    /// The absolute path to the log file.
    /// </summary>
    private static readonly string LOG_FILE_PATH = Path.Combine(AppPaths.ConfigDir, "bcfetch.log");

    /// <summary>
    /// The log file maximum size in bytes.
    /// </summary>
    private const long MAX_LOG_SIZE = 10 * 1024 * 1024; // 10 MB

    public void InitializeLogger()
    {
        var fileTarget = new FileTarget
        {
            FileName = LOG_FILE_PATH,
            Layout = "${longDate}  ${level:uppercase=true:padding=-5:padCharacter= }  ${logger}  ${message:withException=true:exceptionSeparator=  }",
            ArchiveAboveSize = MAX_LOG_SIZE,
            MaxArchiveFiles = 0,
        };

        var config = new LoggingConfiguration();
        config.AddRule(LogLevel.Trace, LogLevel.Fatal, fileTarget);

        LogManager.Configuration = config;
    }
}
