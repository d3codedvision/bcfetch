using BcFetch.IO;
using SkiaSharp;

namespace BcFetch.UnitTests;

public sealed class ImageServiceTests
{
    private static byte[] Png(int width, int height)
    {
        using var bitmap = new SKBitmap(width, height);
        bitmap.Erase(SKColors.Teal);
        using var data = SKImage.FromBitmap(bitmap).Encode(SKEncodedImageFormat.Png, 100);
        return data.ToArray();
    }

    private static (int Width, int Height) SizeOf(byte[] image)
    {
        using var bitmap = SKBitmap.Decode(image);
        return (bitmap.Width, bitmap.Height);
    }

    [Test]
    public async Task ResizeImage_ShrinksToFitAndKeepsAspectRatio()
    {
        var resized = await new ImageService().ResizeImage(Png(1000, 500), 100, 100, CancellationToken.None);

        Assert.That(SizeOf(resized), Is.EqualTo((100, 50)));
    }

    [Test]
    public async Task ResizeImage_NeverUpscales()
    {
        var resized = await new ImageService().ResizeImage(Png(80, 40), 100, 100, CancellationToken.None);

        Assert.That(SizeOf(resized), Is.EqualTo((80, 40)));
    }

    [Test]
    public async Task ConvertToJpegAsync_ProducesAJpeg()
    {
        var jpeg = await new ImageService().ConvertToJpegAsync(Png(20, 20), CancellationToken.None);

        Assert.That(jpeg.Take(2).ToArray(), Is.EqualTo(new byte[] { 0xFF, 0xD8 }));
    }
}
