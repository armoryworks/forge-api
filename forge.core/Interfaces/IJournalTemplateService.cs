using Forge.Core.Models.Accounting;

namespace Forge.Core.Interfaces;

/// <summary>
/// ⚡ Phase-3 — recurring/standard journal templates. Create reusable balanced templates and post normal
/// journal entries from them for a given date (the template drives a <c>PostingRequest</c> through the
/// engine). CAP-ACCT-FULLGL gated at the API edge.
/// </summary>
public interface IJournalTemplateService
{
    Task<JournalTemplateModel> CreateAsync(CreateJournalTemplateModel model, CancellationToken ct = default);
    Task<IReadOnlyList<JournalTemplateModel>> ListAsync(int bookId, CancellationToken ct = default);
    Task<JournalTemplateModel?> GetAsync(int templateId, CancellationToken ct = default);
    Task<PostedFromTemplateModel> PostFromTemplateAsync(
        int templateId, DateOnly entryDate, string? memoOverride, int postedByUserId, CancellationToken ct = default);
}
