namespace BcFetch.IO;

/// <summary>
/// A plain text log in the album folder that records what each run did with that album.
/// Optional (Settings > Downloads); lines are only ever appended.
/// </summary>
internal static class AlbumLog
{
    public const string FileName = "bcfetch-download.log";

    /// <summary>Appends the lines, each stamped with the time; a folder that cannot be written is ignored.</summary>
    public static void Append(string albumDirectory, IEnumerable<string> lines)
    {
        var stamp = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
        try
        {
            File.AppendAllLines(Path.Combine(albumDirectory, FileName), lines.Select(l => $"[{stamp}] {l}"));
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }
}
