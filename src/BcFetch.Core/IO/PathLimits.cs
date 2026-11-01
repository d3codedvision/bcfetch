using System.Text;

namespace BcFetch.IO;

/// <summary>
/// Linux limits every path component to 255 bytes (not characters), so a long title in a non-Latin script
/// is the usual way to hit it. Components are cut at a character boundary; the folder path is never cut as a whole.
/// </summary>
internal static class PathLimits
{
    private const int MaxComponentBytes = 255;

    // Room for the suffixes added next to a file while it downloads (".download" and friends).
    private const int SuffixReserve = 16;

    /// <summary>Shortens every component of a folder path to fit.</summary>
    public static string FitPath(string path)
    {
        return string.Join(Path.DirectorySeparatorChar, path.Split(Path.DirectorySeparatorChar).Select(c => Truncate(c, MaxComponentBytes).TrimEnd(' ', '.')));
    }

    /// <summary>Shortens a file name to fit, keeping its extension.</summary>
    public static string FitFileName(string fileName)
    {
        var budget = MaxComponentBytes - SuffixReserve;
        if (Encoding.UTF8.GetByteCount(fileName) <= budget)
        {
            return fileName;
        }

        var extension = Path.GetExtension(fileName);
        var stem = fileName[..^extension.Length];
        return Truncate(stem, budget - Encoding.UTF8.GetByteCount(extension)).TrimEnd(' ', '.') + extension;
    }

    private static string Truncate(string text, int maxBytes)
    {
        if (Encoding.UTF8.GetByteCount(text) <= maxBytes)
        {
            return text;
        }

        var result = new StringBuilder();
        var bytes = 0;
        foreach (var rune in text.EnumerateRunes())
        {
            bytes += rune.Utf8SequenceLength;
            if (bytes > maxBytes)
            {
                break;
            }

            result.Append(rune.ToString());
        }

        return result.ToString();
    }
}
