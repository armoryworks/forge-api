using FluentAssertions;
using SkiaSharp;

using Forge.Api.Services;

namespace Forge.Tests.Services;

public class ImageServiceTests
{
    private readonly ImageService _service = new();

    private static MemoryStream Png(int width, int height, SKColor colour)
    {
        using var bitmap = new SKBitmap(width, height);
        using (var canvas = new SKCanvas(bitmap)) canvas.Clear(colour);
        using var image = SKImage.FromBitmap(bitmap);
        using var data = image.Encode(SKEncodedImageFormat.Png, 100);
        return new MemoryStream(data.ToArray());
    }

    private static (int Width, int Height, SKEncodedImageFormat Format) Inspect(byte[] bytes)
    {
        using var codec = SKCodec.Create(SKData.CreateCopy(bytes));
        return (codec.Info.Width, codec.Info.Height, codec.EncodedFormat);
    }

    [Fact]
    public async Task Thumbnail_fits_the_box_and_keeps_the_aspect_ratio()
    {
        var result = Inspect(await _service.GenerateThumbnailAsync(Png(800, 400, SKColors.Red)));

        result.Width.Should().Be(200);
        result.Height.Should().Be(100);
        result.Format.Should().Be(SKEncodedImageFormat.Jpeg);
    }

    [Fact]
    public async Task Dimensions_are_read_without_changing_the_image()
    {
        var (width, height) = await _service.GetDimensionsAsync(Png(321, 123, SKColors.Blue));

        width.Should().Be(321);
        height.Should().Be(123);
    }

    [Fact]
    public async Task Convert_produces_a_jpeg_of_the_same_size()
    {
        var result = Inspect(await _service.ConvertToJpegAsync(Png(64, 48, SKColors.Green), 90));

        result.Should().Be((64, 48, SKEncodedImageFormat.Jpeg));
    }

    [Fact]
    public async Task Transparent_pixels_become_white_not_black()
    {
        var jpeg = await _service.ConvertToJpegAsync(Png(10, 10, SKColors.Transparent));

        using var decoded = SKBitmap.Decode(jpeg);
        var pixel = decoded.GetPixel(5, 5);
        pixel.Red.Should().BeGreaterThan(240);
        pixel.Green.Should().BeGreaterThan(240);
        pixel.Blue.Should().BeGreaterThan(240);
    }

    [Fact]
    public async Task A_file_that_is_not_an_image_is_rejected()
    {
        var act = () => _service.GetDimensionsAsync(new MemoryStream("not an image"u8.ToArray()));

        await act.Should().ThrowAsync<InvalidDataException>();
    }
}
