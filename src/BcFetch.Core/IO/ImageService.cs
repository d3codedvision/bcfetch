using SkiaSharp;

namespace BcFetch.IO;

internal interface IImageService
{
    Task<byte[]> ConvertToJpegAsync(byte[] inputImage, CancellationToken cancellationToken);
    Task<byte[]> ResizeImage(byte[] inputImage, int maxWidth, int maxHeight, CancellationToken cancellationToken);
}

internal sealed class ImageService : IImageService
{
    public Task<byte[]> ConvertToJpegAsync(byte[] inputImage, CancellationToken cancellationToken)
    {
        return Task.Run(() =>
        {
            using var bitmap = Decode(inputImage);
            return Encode(bitmap, SKEncodedImageFormat.Jpeg);
        }, cancellationToken);
    }

    public Task<byte[]> ResizeImage(byte[] inputImage, int maxWidth, int maxHeight, CancellationToken cancellationToken)
    {
        return Task.Run(() =>
        {
            using var codec = SKCodec.Create(new MemoryStream(inputImage));
            var format = codec?.EncodedFormat ?? SKEncodedImageFormat.Jpeg;
            using var bitmap = Decode(inputImage);

            // Shrink to fit in the box, keep aspect ratio, never upscale.
            var scale = Math.Min((double)maxWidth / bitmap.Width, (double)maxHeight / bitmap.Height);
            if (scale >= 1)
            {
                return Encode(bitmap, format);
            }

            var info = new SKImageInfo(Math.Max(1, (int)(bitmap.Width * scale)), Math.Max(1, (int)(bitmap.Height * scale)));
            using var resized = bitmap.Resize(info, new SKSamplingOptions(SKCubicResampler.Mitchell));
            return Encode(resized, format);
        }, cancellationToken);
    }

    private static SKBitmap Decode(byte[] data) => SKBitmap.Decode(data) ?? throw new InvalidDataException("Unsupported image data.");

    private static byte[] Encode(SKBitmap bitmap, SKEncodedImageFormat format)
    {
        using var image = SKImage.FromBitmap(bitmap);
        using var data = image.Encode(format, 90) ?? image.Encode(SKEncodedImageFormat.Jpeg, 90);
        return data.ToArray();
    }
}
