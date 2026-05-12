namespace Forge.Core.Interfaces;

/// <summary>
/// Fills AcroForm fields in a government-issued PDF template (W-4, I-9, state withholding)
/// with collected form data, then optionally flattens the result.
/// </summary>
public interface IPdfFormFillService
{
    /// <summary>
    /// Fill AcroForm fields in a PDF template with the provided field values.
    /// </summary>
    /// <param name="templateBytes">The blank/original government PDF bytes.</param>
    /// <param name="fieldValues">
    /// Dictionary mapping AcroForm field names to string values.
    /// Field names must match the names in the PDF's AcroForm dictionary.
    /// </param>
    /// <param name="flatten">
    /// When true, flatten the form after filling (fields become non-editable).
    /// Use false for I-9 Section 1 (Section 2 fields still need to be filled later).
    /// Use true for W-4, state withholding, and I-9 Section 2 (final fill).
    /// </param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The filled (and optionally flattened) PDF bytes.</returns>
    Task<byte[]> FillFormAsync(
        byte[] templateBytes,
        Dictionary<string, string> fieldValues,
        bool flatten,
        CancellationToken ct);

    /// <summary>
    /// List all AcroForm field names in a PDF.
    /// Useful for building the AcroFieldMapJson configuration.
    /// </summary>
    Task<IReadOnlyList<string>> GetFieldNamesAsync(byte[] pdfBytes, CancellationToken ct);
}
