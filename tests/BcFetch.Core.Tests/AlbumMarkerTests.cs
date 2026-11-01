using BcFetch.IO;

namespace BcFetch.UnitTests;

public sealed class AlbumMarkerTests
{
    [Test]
    public void IsComplete_RequiresMarkerAndEveryTrackFile()
    {
        var dir = Directory.CreateTempSubdirectory().FullName;
        var track = Path.Combine(dir, "01.mp3");
        File.WriteAllText(track, "x");

        Assert.That(AlbumMarker.IsComplete(dir, [track]), Is.False, "no marker yet");

        AlbumMarker.Mark(dir);
        Assert.That(AlbumMarker.IsComplete(dir, [track]), Is.True);
        Assert.That(AlbumMarker.IsComplete(dir, [track, Path.Combine(dir, "02.mp3")]), Is.False, "a track is missing");

        Directory.Delete(dir, true);
    }
}

public sealed class UrlHelperTests
{
    [Test]
    public void ExtractUrls_KeepsDistinctHttpUrlsInOrder()
    {
        const string text = "# comment\nhttps://a.bandcamp.com/album/x\r\nnot a url\nhttps://a.bandcamp.com/album/x ftp://x/y http://b.bandcamp.com";

        Assert.That(BcFetch.Helpers.UrlHelper.ExtractUrls(text), Is.EqualTo(new[] { "https://a.bandcamp.com/album/x", "http://b.bandcamp.com" }));
        Assert.That(BcFetch.Helpers.UrlHelper.ExtractUrls(null), Is.Empty);
    }
}
