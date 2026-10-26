namespace Forge.Core.Models.Accounting;

/// <summary>
/// Replace a journal template's header and lines. <paramref name="IsActive"/> false keeps the template on file
/// but refuses posting from it.
/// </summary>
public sealed record UpdateJournalTemplateModel(
    string Name,
    string? Description,
    string? Memo,
    bool AutoReverseNextPeriod,
    bool IsActive,
    List<JournalTemplateLineModel> Lines);
