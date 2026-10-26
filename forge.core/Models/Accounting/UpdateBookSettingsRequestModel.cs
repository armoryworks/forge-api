namespace Forge.Core.Models.Accounting;

/// <summary>
/// Body of <c>PUT accounting/books/{id}/settings</c>. A null threshold turns maker-checker off; a null
/// time zone leaves the book's zone as it is.
/// </summary>
public record UpdateBookSettingsRequestModel(decimal? MakerCheckerThreshold, string? ReportingTimeZone);
