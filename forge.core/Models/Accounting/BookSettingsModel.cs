namespace Forge.Core.Models.Accounting;

/// <summary>
/// A book's editable settings: the maker-checker threshold (null = off) and the reporting time zone, which
/// is locked once the book has a posted entry.
/// </summary>
public record BookSettingsModel(
    int BookId,
    string Code,
    string Name,
    decimal? MakerCheckerThreshold,
    string ReportingTimeZone,
    bool TimeZoneLocked,
    string? CompanyTimeZone);
