using System.Diagnostics;

namespace BcFetch.Helpers;

internal static class UrlHelper
{
    /// <summary>
    /// Returns the distinct http(s) URLs found in free text (one dropped link, a text/uri-list, a pasted
    /// paragraph or a file of URLs), in order. Comment lines and anything that is not a URL are ignored.
    /// </summary>
    public static IReadOnlyList<string> ExtractUrls(string text)
    {
        var urls = new List<string>();
        foreach (var token in (text ?? "").Split((char[])null, StringSplitOptions.RemoveEmptyEntries))
        {
            if (Uri.TryCreate(token, UriKind.Absolute, out var uri) && uri.Scheme is "http" or "https" && !urls.Contains(token))
            {
                urls.Add(token);
            }
        }

        return urls;
    }

    public static void OpenUrlInBrowser(string url)
    {
        using var p = new Process();
        p.StartInfo.FileName = url;
        p.StartInfo.UseShellExecute = true;
        p.Start();
    }
}
