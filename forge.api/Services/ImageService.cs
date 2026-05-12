using Forge.Core.Interfaces;

using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats.Jpeg;
using SixLabors.ImageSharp.Processing;

namespace Forge.Api.Services;

public class ImageService : IImageService
{
    public async Task<byte[]> GenerateThumbnailAsync(Stream imageStream, int maxWidth = 200, int maxHeight = 200)
    {
        using var image = await Image.LoadAsync(imageStream);
        image.Mutate(x => x.Resize(new ResizeOptions
        {
            Size = new Size(maxWidth, maxHeight),
            Mode = ResizeMode.Max,
        }));

        using var output = new MemoryStream();
        await image.SaveAsJpegAsync(output, new JpegEncoder { Quality = 80 });
        return output.ToArray();
    }

    public async Task<(int Width, int Height)> GetDimensionsAsync(Stream imageStream)
    {
        using var image = await Image.LoadAsync(imageStream);
        return (image.Width, image.Height);
    }

    public async Task<byte[]> ConvertToJpegAsync(Stream imageStream, int quality = 80)
    {
        using var image = await Image.LoadAsync(imageStream);
        using var output = new MemoryStream();
        await image.SaveAsJpegAsync(output, new JpegEncoder { Quality = quality });
        return output.ToArray();
    }
}
