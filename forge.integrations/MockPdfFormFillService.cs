using Microsoft.Extensions.Logging;

using Forge.Core.Interfaces;

namespace Forge.Integrations;

/// <summary>
/// Mock implementation of IPdfFormFillService for development/testing.
/// Returns the original PDF unchanged (no actual field filling).
/// Logs what would have been filled.
/// </summary>
public class MockPdfFormFillService(ILogger<MockPdfFormFillService> logger) : IPdfFormFillService
{
    public Task<byte[]> FillFormAsync(
        byte[] templateBytes,
        Dictionary<string, string> fieldValues,
        bool flatten,
        CancellationToken ct)
    {
        logger.LogInformation(
            "[MOCK] FillFormAsync called with {Count} fields, flatten={Flatten}. Fields: {Fields}",
            fieldValues.Count,
            flatten,
            string.Join(", ", fieldValues.Select(kv => $"{kv.Key}={kv.Value}")));

        // Return original bytes unchanged — no real AcroForm manipulation
        return Task.FromResult(templateBytes);
    }

    public Task<IReadOnlyList<string>> GetFieldNamesAsync(byte[] pdfBytes, CancellationToken ct)
    {
        logger.LogInformation("[MOCK] GetFieldNamesAsync called — returning empty list.");
        return Task.FromResult<IReadOnlyList<string>>(Array.Empty<string>());
    }
}
