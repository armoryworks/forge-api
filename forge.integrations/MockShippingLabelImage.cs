using System.Security.Cryptography;
using System.Text;

using SkiaSharp;

namespace Forge.Integrations;

/// <summary>
/// Draws the placeholder 4x6 label PNG the mock shipping service returns, so the ship-document flow
/// can be exercised end to end without a carrier sandbox. The bar pattern is derived from the
/// tracking number, so two mock labels look different; it is not a scannable barcode.
/// </summary>
public static class MockShippingLabelImage
{
    private const int Width = 400;
    private const int Height = 600;

    public static byte[] Render(string trackingNumber)
    {
        using var bitmap = new SKBitmap(Width, Height, SKColorType.Rgba8888, SKAlphaType.Opaque);
        using (var canvas = new SKCanvas(bitmap))
        {
            canvas.Clear(SKColors.White);
            using var ink = new SKPaint { Color = SKColors.Black, IsAntialias = false };
            using var frame = new SKPaint { Color = SKColors.Black, Style = SKPaintStyle.Stroke, StrokeWidth = 6 };

            canvas.DrawRect(10, 10, Width - 20, Height - 20, frame);
            canvas.DrawRect(10, 200, Width - 20, 4, ink);
            canvas.DrawRect(10, 420, Width - 20, 4, ink);

            var hash = SHA256.HashData(Encoding.UTF8.GetBytes(trackingNumber));
            var x = 30f;
            foreach (var b in hash)
            {
                for (var bit = 0; bit < 8 && x < Width - 30; bit += 2)
                {
                    var barWidth = 2 + ((b >> bit) & 0x3);
                    canvas.DrawRect(x, 450, barWidth, 110, ink);
                    x += barWidth + 3;
                }
            }
        }

        using var image = SKImage.FromBitmap(bitmap);
        using var data = image.Encode(SKEncodedImageFormat.Png, 100);
        return data.ToArray();
    }
}
