using System.Globalization;
using System.Text.Json;

using Microsoft.EntityFrameworkCore;

using Forge.Api.Capabilities;
using Forge.Api.Services;
using Forge.Core.Entities;
using Forge.Core.Entities.Accounting;
using Forge.Core.Enums;
using Forge.Core.Enums.Accounting;
using Forge.Core.Interfaces;
using Forge.Core.Models.Accounting;
using Forge.Data.Context;
using Serilog;

namespace Forge.Api.Features.Accounting;

/// <summary>
/// Phase-2 STAGE E — material-issue WIP posting (ACCOUNTING_SUITE_PLAN §7 "material issue", §8.1). When raw
/// material / components are issued to a job, this service posts the inventory→WIP move <b>inline, in the
/// material-issue command's transaction</b>, valued at the <b>perpetual weighted-average</b> from the
/// valuation store (the cost receipts feed via <see cref="IInventoryValuationService.ApplyReceiptAsync"/>):
/// <list type="bullet">
///   <item><b>Issue</b> → Dr INVENTORY_WIP / Cr INVENTORY_{RAW|WIP|FG} (the issued part's class); the store is
///         relieved via <c>ApplyIssueAsync</c> and that relieved value is the posted amount.</item>
///   <item><b>Scrap</b> → Dr OPERATING_EXPENSE / Cr INVENTORY_… (consumed, not capitalized into WIP).</item>
///   <item><b>Return</b> → Dr INVENTORY_… / Cr INVENTORY_WIP (the reverse of an issue); the store is
///         re-credited via <c>ApplyReceiptAsync</c> at the issue's recorded unit cost.</item>
/// </list>
///
/// <para><b>STAYS DARK while CAP-ACCT-FULLGL is OFF (the default)</b> — the first check returns a no-op, so the
/// operational material-issue flow (bin decrement + <see cref="MaterialIssue"/> row) is unchanged. Idempotent
/// via the engine's (BookId, IdempotencyKey) de-dupe keyed on the issue id; because the valuation-store
/// mutation runs <i>outside</i> the engine, the service also pre-checks for an existing entry and bails before
/// touching the store, so a re-post can't double-relieve. When the store has no row for the part (e.g.
/// pre-go-live, or a part never received through the perpetual path) it falls back to the issue's recorded unit
/// cost and leaves the store untouched.</para>
/// </summary>
public interface IMaterialIssuePostingService
{
    /// <summary>
    /// Posts the WIP / inventory journal for a persisted <see cref="MaterialIssue"/>, when (and only when)
    /// CAP-ACCT-FULLGL is enabled. A no-op while the capability is off, for a non-stocked part, or a
    /// zero-value issue.
    /// </summary>
    Task PostMaterialIssueAsync(
        int materialIssueId, DateOnly entryDate, int issuedByUserId, CancellationToken ct = default);
}

/// <inheritdoc />
public sealed class MaterialIssuePostingService(
    AppDbContext db,
    IPostingEngine postingEngine,
    ICapabilitySnapshotProvider capabilities,
    ISystemAuditWriter? auditWriter = null,
    IInventoryValuationService? valuation = null) : IMaterialIssuePostingService
{
    private const string FullGlCapability = "CAP-ACCT-FULLGL";

    private const string KeyInventoryRaw = "INVENTORY_RAW";
    private const string KeyInventoryWip = "INVENTORY_WIP";
    private const string KeyInventorySubassembly = "INVENTORY_SUBASSEMBLY";
    private const string KeyInventoryFg = "INVENTORY_FG";
    private const string KeyOperatingExpense = "OPERATING_EXPENSE";

    public async Task PostMaterialIssueAsync(
        int materialIssueId, DateOnly entryDate, int issuedByUserId, CancellationToken ct = default)
    {
        if (!capabilities.IsEnabled(FullGlCapability))
            return;

        var issue = await db.Set<MaterialIssue>()
            .Include(i => i.Part)
            .FirstOrDefaultAsync(i => i.Id == materialIssueId, ct);
        if (issue is null)
        {
            Log.Warning("Material-issue posting skipped: issue {IssueId} not found (FULLGL on).", materialIssueId);
            return;
        }

        // Only perpetual-stocked parts have an inventory ledger to move; consumables/tools were expensed at
        // receipt, so issuing them to a job has no INVENTORY_* relief.
        if (!IsStocked(issue.Part) || issue.Quantity <= 0m)
            return;

        var book = await db.Books.AsNoTracking()
            .Where(b => b.IsActive).OrderBy(b => b.Id).FirstOrDefaultAsync(ct);
        if (book is null)
            throw new PostingException(
                "NO_POSTING_BOOK",
                "CAP-ACCT-FULLGL is enabled but no active accounting Book is seeded to post the material issue into.");

        var idempotencyKey = $"{JournalSource.Inventory}:MaterialIssue:{issue.Id}";

        // Idempotency guard for the store side-effect: the engine de-dupes the journal entry, but the
        // ApplyIssue/ApplyReceipt mutation runs outside it — bail before touching the store if this issue is
        // already posted.
        var alreadyPosted = await db.JournalEntries.IgnoreQueryFilters()
            .AnyAsync(e => e.BookId == book.Id && e.IdempotencyKey == idempotencyKey, ct);
        if (alreadyPosted)
            return;

        var reliefKey = ReliefKeyFor(issue.Part);  // the issued part's own inventory account
        var isReturn = issue.IssueType == MaterialIssueType.Return;

        // Perpetual value: relieve (or, for a return, re-credit) the valuation store at weighted-average when a
        // row exists; otherwise fall back to the issue's recorded unit cost and leave the store untouched.
        decimal amount;
        if (isReturn)
        {
            amount = Math.Round(issue.Quantity * issue.UnitCost, 2);
            if (amount <= 0m)
                return;
            if (valuation is not null)
                await valuation.ApplyReceiptAsync(book.Id, issue.PartId, issue.Quantity, amount, ct);
        }
        else
        {
            var hasStoreRow = valuation is not null
                && await db.InventoryValuations.AnyAsync(v => v.BookId == book.Id && v.PartId == issue.PartId, ct);
            if (hasStoreRow)
            {
                amount = await valuation!.ApplyIssueAsync(book.Id, issue.PartId, issue.Quantity, ct);
            }
            else
            {
                amount = Math.Round(issue.Quantity * issue.UnitCost, 2);
            }
            if (amount <= 0m)
                return;
        }

        // Issue : Dr WIP / Cr <part inventory>   Scrap : Dr OPERATING_EXPENSE / Cr <part inventory>
        // Return: Dr <part inventory> / Cr WIP   (reverse of an issue)
        string debitKey, creditKey;
        if (isReturn)
            (debitKey, creditKey) = (reliefKey, KeyInventoryWip);
        else if (issue.IssueType == MaterialIssueType.Scrap)
            (debitKey, creditKey) = (KeyOperatingExpense, reliefKey);
        else
            (debitKey, creditKey) = (KeyInventoryWip, reliefKey);

        var partLabel = issue.Part?.PartNumber ?? $"part {issue.PartId}";
        var verb = issue.IssueType.ToString().ToLowerInvariant();
        var desc = $"Material {verb} — job {issue.JobId} {partLabel} x{issue.Quantity}";

        var request = new PostingRequest
        {
            BookId = book.Id,
            EntryDate = entryDate,
            Source = JournalSource.Inventory,
            SourceType = "MaterialIssue",
            SourceId = issue.Id,
            CurrencyId = book.FunctionalCurrencyId,
            Memo = $"Material {verb} — job {issue.JobId}, {partLabel}",
            IdempotencyKey = idempotencyKey,
            Lines =
            [
                // Tag the WIP leg with the Job dimension so GL WIP is isolatable per job (the job-cost close /
                // production-variance sweep reads WIP-by-job). The inventory leg is stock, not job-specific.
                new PostingLine { AccountKey = debitKey, Debit = amount, JobId = debitKey == KeyInventoryWip ? issue.JobId : null, Description = desc },
                new PostingLine { AccountKey = creditKey, Credit = amount, JobId = creditKey == KeyInventoryWip ? issue.JobId : null, Description = desc },
            ],
        };

        var entry = await postingEngine.PostAsync(request, issuedByUserId, ct);
        await TryAuditAsync(issue, entry, amount, issuedByUserId, ct);
    }

    /// <summary>True for perpetual-stocked inventory classes (the ones backed by an INVENTORY_* account).</summary>
    private static bool IsStocked(Part? part) => part?.InventoryClass is
        InventoryClass.Raw or InventoryClass.Component or InventoryClass.Subassembly or InventoryClass.FinishedGood;

    /// <summary>Maps the issued part's <see cref="InventoryClass"/> to the inventory determination key it
    /// relieves. A null/unknown class defaults to raw-materials inventory (the usual job input).</summary>
    private static string ReliefKeyFor(Part? part) => part?.InventoryClass switch
    {
        InventoryClass.Raw or InventoryClass.Component => KeyInventoryRaw,
        InventoryClass.Subassembly => KeyInventorySubassembly,
        InventoryClass.FinishedGood => KeyInventoryFg,
        _ => KeyInventoryRaw,
    };

    private async Task TryAuditAsync(
        MaterialIssue issue, JournalEntry entry, decimal amount, int actorUserId, CancellationToken ct)
    {
        if (auditWriter is null)
            return;

        try
        {
            var details = JsonSerializer.Serialize(new
            {
                before = (object?)null,
                after = new
                {
                    journalEntryId = entry.Id,
                    entryNumber = entry.EntryNumber,
                    bookId = entry.BookId,
                    materialIssueId = issue.Id,
                    jobId = issue.JobId,
                    partId = issue.PartId,
                    issueType = issue.IssueType.ToString(),
                    quantity = issue.Quantity,
                    amount = amount.ToString(CultureInfo.InvariantCulture),
                },
                reason = $"Material {issue.IssueType.ToString().ToLowerInvariant()} — job {issue.JobId} WIP posting.",
            });

            await auditWriter.WriteAsync(
                action: "GlMaterialIssuePosted",
                userId: actorUserId,
                entityType: nameof(JournalEntry),
                entityId: null,
                details: details,
                ct: ct);
        }
        catch (Exception ex)
        {
            Log.Warning(ex,
                "Material-issue posting audit write failed for issue {IssueId} (entry {EntryId}); posting itself is committed.",
                issue.Id, entry.Id);
        }
    }
}
