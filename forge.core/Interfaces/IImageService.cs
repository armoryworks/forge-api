namespace Forge.Core.Interfaces;

public interface IImageService
{
    Task<byte[]> GenerateThumbnailAsync(Stream imageStream, int maxWidth = 200, int maxHeight = 200);
    Task<(int Width, int Height)> GetDimensionsAsync(Stream imageStream);
    Task<byte[]> ConvertToJpegAsync(Stream imageStream, int quality = 80);
}
