using System;
using System.Reflection;

namespace BcFetch.Core;

internal static class Constants
{
    /// <summary>GitHub "owner/name" of the project. Every URL below derives from it.</summary>
    public const string GITHUB_REPO = "d3codedvision/bcfetch";

    /// <summary>
    /// The version of BcFetch.
    /// </summary>
    public static readonly Version APP_VERSION = Assembly.GetEntryAssembly()?.GetName().Version;

    /// <summary>
    /// The formatted version (X.Y.Z) of BcFetch.
    /// </summary>
    public static readonly string APP_VERSION_FORMATTED = APP_VERSION.ToString(3);

    /// <summary>
    /// The URL redirecting to the help page on translating the app on GitHub.
    /// </summary>
    public const string URL_HELP_TRANSLATE = $"https://github.com/{GITHUB_REPO}/blob/main/docs/help-translate.md";

    /// <summary>
    /// The URL redirecting to the issues page on GitHub.
    /// </summary>
    public const string URL_ISSUES = $"https://github.com/{GITHUB_REPO}/issues";

    /// <summary>
    /// The URL redirecting to the latest release on GitHub.
    /// </summary>
    public const string URL_LATEST_RELEASE = $"https://github.com/{GITHUB_REPO}/releases/latest";

    /// <summary>
    /// The URL redirecting to the releases page on GitHub.
    /// </summary>
    public const string URL_RELEASES = $"https://github.com/{GITHUB_REPO}/releases";

    /// <summary>
    /// The website URL of BcFetch.
    /// </summary>
    public const string URL_WEBSITE = $"https://github.com/{GITHUB_REPO}";
}
