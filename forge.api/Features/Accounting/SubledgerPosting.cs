using Forge.Core.Entities.Accounting;
using Forge.Core.Enums.Accounting;
using Forge.Core.Interfaces;
using Forge.Core.Models.Accounting;

namespace Forge.Api.Features.Accounting;

/// <summary>
/// The sub-ledger posting path (§5.7, owner decision 2026-10-16). A journal caused by a business action
/// (PO receipt, vendor bill approval, invoice send, material issue, production receipt, expense approval,
/// customer and vendor payments, and their voids) posts as the system principal: the action's own role and
/// capability check is the authorization, so the user who performed it does not also need
/// <see cref="GlCapability.PostJournalEntry"/> or <see cref="GlCapability.ReverseJournalEntry"/>. The acting
/// user is still recorded as <c>PostedBy</c>. Manual journal entries never use this path; they post through
/// <see cref="IPostingEngine"/> directly and keep the per-user check.
/// </summary>
public static class SubledgerPosting
{
    /// <summary>Posts a sub-ledger entry inside <see cref="GlSystemPostingScope"/>.</summary>
    /// <exception cref="ArgumentException">The request is a manual journal.</exception>
    public static async Task<JournalEntry> PostAsSystemAsync(
        this IPostingEngine engine, PostingRequest request, int actingUserId, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (request.Source == JournalSource.Manual)
            throw new ArgumentException("A manual journal entry cannot post as the system principal.", nameof(request));

        using (GlSystemPostingScope.Enter())
            return await engine.PostAsync(request, actingUserId, ct);
    }

    /// <summary>Reverses a sub-ledger entry inside <see cref="GlSystemPostingScope"/>.</summary>
    public static async Task<JournalEntry> ReverseAsSystemAsync(
        this IPostingEngine engine, long entryId, DateOnly reversalDate, string reason, int actingUserId,
        CancellationToken ct = default)
    {
        using (GlSystemPostingScope.Enter())
            return await engine.ReverseAsync(entryId, reversalDate, reason, actingUserId, ct);
    }
}
