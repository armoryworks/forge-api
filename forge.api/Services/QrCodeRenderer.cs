using QRCoder;

namespace Forge.Api.Services;

/// <summary>
/// Renders a QR code to PNG bytes for embedding in server-generated PDFs (packing slips, labels).
/// Uses QRCoder's <see cref="PngByteQRCode"/> — pure managed code with no System.Drawing / libgdiplus
/// dependency, so it works in the Linux API container. The bytes drop straight into QuestPDF's
/// <c>.Image(byte[])</c>.
/// </summary>
public static class QrCodeRenderer
{
    /// <param name="text">The value to encode (e.g. a shipment ScanCode).</param>
    /// <param name="pixelsPerModule">Module size; larger = crisper at print scale. 6 is plenty for a label QR.</param>
    public static byte[] Png(string text, int pixelsPerModule = 6)
    {
        using var generator = new QRCodeGenerator();
        // ECC level M (~15% recovery) — a sensible balance of density vs. smudge tolerance on a printed label.
        using var data = generator.CreateQrCode(text, QRCodeGenerator.ECCLevel.M);
        return new PngByteQRCode(data).GetGraphic(pixelsPerModule);
    }
}
