namespace Forge.Core.Models.Accounting;

/// <summary>
/// A finished CSV export ready for an HTTP file response (QB-001 CPA downstream).
/// Content type is always <c>text/csv</c>; the controller passes
/// <see cref="FileName"/> through <c>Content-Disposition</c>.
/// </summary>
public record CsvExportResult(byte[] Content, string FileName);
