using System.Text;
using BcFetch.IO;

namespace BcFetch.UnitTests;

public sealed class PathLimitsTests
{
    [Test]
    public void FitFileName_ShortNameIsUnchanged()
    {
        Assert.That(PathLimits.FitFileName("01 Artist - Title.mp3"), Is.EqualTo("01 Artist - Title.mp3"));
    }

    [Test]
    public void FitFileName_KeepsExtensionAndStaysWithinBytes_ForMultiByteText()
    {
        var name = new string('Ω', 300) + ".mp3"; // 2 bytes per character

        var fitted = PathLimits.FitFileName(name);

        Assert.That(fitted, Does.EndWith(".mp3"));
        Assert.That(Encoding.UTF8.GetByteCount(fitted), Is.LessThanOrEqualTo(255 - 9)); // room for ".download"
        Assert.That(fitted.Contains('�'), Is.False, "no character was cut in half");
    }

    [Test]
    public void FitPath_ShortensEachComponentAndKeepsTheRest()
    {
        var album = new string('a', 400);

        var fitted = PathLimits.FitPath($"/home/me/Music/Artist/{album}");

        Assert.That(fitted, Does.StartWith("/home/me/Music/Artist/"));
        Assert.That(fitted.Split('/').Max(c => c.Length), Is.EqualTo(255));
    }
}
