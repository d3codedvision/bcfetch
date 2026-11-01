namespace BcFetch.IO;

/// <summary>
/// A hidden file in an album folder, written once every track of the album is on disk.
/// Later runs skip such albums before any size lookups or downloads.
/// </summary>
internal static class AlbumMarker
{
    public const string FileName = ".bcfetch-done";

    /// <summary>True when the marker exists and no track file has been deleted since.</summary>
    public static bool IsComplete(string albumDirectory, IEnumerable<string> trackPaths)
    {
        return File.Exists(Path.Combine(albumDirectory, FileName)) && trackPaths.All(File.Exists);
    }

    /// <summary>Writes the marker; a folder that cannot be written only means the album is not skipped next time.</summary>
    public static void Mark(string albumDirectory)
    {
        try
        {
            File.WriteAllText(Path.Combine(albumDirectory, FileName), string.Empty);
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }
}
