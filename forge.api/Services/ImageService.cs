using SkiaSharp;

using Forge.Core.Interfaces;

namespace Forge.Api.Services;

public class ImageService : IImageService
{
    public async Task<byte[]> GenerateThumbnailAsync(Stream imageStream, int maxWidth = 200, int maxHeight = 200)
    {
        using var source = await DecodeAsync(imageStream);
        var scale = Math.Min((double)maxWidth / source.Width, (double)maxHeight / source.Height);
        var width = Math.Max(1, (int)Math.Round(source.Width * scale));
        var height = Math.Max(1, (int)Math.Round(source.Height * scale));

        using var resized = source.Resize(new SKImageInfo(width, height), new SKSamplingOptions(SKCubicResampler.Mitchell))
            ?? throw new InvalidOperationException("The image could not be resized.");
        return EncodeJpeg(resized, 80);
    }

    public async Task<(int Width, int Height)> GetDimensionsAsync(Stream imageStream)
    {
        using var data = await ReadAsync(imageStream);
        using var codec = SKCodec.Create(data)
            ?? throw new InvalidDataException("The file is not a supported image.");
        return (codec.Info.Width, codec.Info.Height);
    }

    public async Task<byte[]> ConvertToJpegAsync(Stream imageStream, int quality = 80)
    {
        using var source = await DecodeAsync(imageStream);
        return EncodeJpeg(source, quality);
    }

    private static async Task<SKBitmap> DecodeAsync(Stream imageStream)
    {
        using var data = await ReadAsync(imageStream);
        return SKBitmap.Decode(data)
            ?? throw new InvalidDataException("The file is not a supported image.");
    }

    private static async Task<SKData> ReadAsync(Stream imageStream)
    {
        using var buffer = new MemoryStream();
        await imageStream.CopyToAsync(buffer);
        return SKData.CreateCopy(buffer.ToArray());
    }

    private static byte[] EncodeJpeg(SKBitmap bitmap, int quality)
    {
        using var flattened = new SKBitmap(bitmap.Width, bitmap.Height, SKColorType.Rgba8888, SKAlphaType.Opaque);
        using (var canvas = new SKCanvas(flattened))
        {
            canvas.Clear(SKColors.White);
            canvas.DrawBitmap(bitmap, 0, 0);
        }
        using var image = SKImage.FromBitmap(flattened);
        using var encoded = image.Encode(SKEncodedImageFormat.Jpeg, quality);
        return encoded.ToArray();
    }
}
