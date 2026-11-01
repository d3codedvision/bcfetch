using BcFetch.IO;

namespace BcFetch.UnitTests;

public sealed class AlbumLogTests
{
    [Test]
    public void Append_StampsEveryLineAndKeepsEarlierRuns()
    {
        var dir = Directory.CreateTempSubdirectory().FullName;

        AlbumLog.Append(dir, ["first run", "  downloaded 01.mp3"]);
        AlbumLog.Append(dir, ["second run"]);

        var lines = File.ReadAllLines(Path.Combine(dir, AlbumLog.FileName));
        Assert.That(lines, Has.Length.EqualTo(3));
        Assert.That(lines[0], Does.Match(@"^\[\d{4}-\d\d-\d\d \d\d:\d\d:\d\d\] first run$"));
        Assert.That(lines[2], Does.EndWith("] second run"));

        Directory.Delete(dir, true);
    }

    [Test]
    public void Append_IgnoresAFolderThatDoesNotExist()
    {
        Assert.DoesNotThrow(() => AlbumLog.Append(Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString()), ["x"]));
    }
}
