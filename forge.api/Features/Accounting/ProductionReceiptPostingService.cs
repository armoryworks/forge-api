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
/// Phase-2 STAGE E — production receipt (job-complete → finished goods). When a completed
/// <see cref="ProductionRun"/>'s good output is received into stock, this service posts the WIP→FG move
/// <b>inline, in the receive-to-stock command's transaction</b>, at the produced part's <b>standard cost</b>:
/// <list type="bullet">
///   <item><b>Dr INVENTORY_{FG|SUBASSEMBLY|RAW}</b> (the produced part's class) — usually INVENTORY_FG.</item>
///   <item><b>Cr INVENTORY_WIP</b> — relieving the job's accumulated work-in-process.</item>
/// </list>
/// and feeds the perpetual FG valuation store via <see cref="IInventoryValuationService.ApplyReceiptAsync"/>.
///
/// <para><b>Costing — standard.</b> FG is valued at <c>standard cost × good qty</c> (the documented
/// manufacturing default). Any difference between the standard FG value credited out of WIP and the actual
/// WIP accumulated (material issues at weighted-average + labor) <b>remains in INVENTORY_WIP as a production
/// variance</b>, recognized at period-end variance analysis — symmetric with the PO-receipt service deferring
/// PPV to the 3-way match. A part with no resolvable standard cost is stocked operationally but its GL/value
/// is skipped (logged), so a missing cost never blocks the receipt.</para>
///
/// <para><b>STAYS DARK while CAP-ACCT-FULLGL is OFF (the default)</b> — a no-op on the first check, so the
/// operational FG stocking is unchanged. Idempotent via the engine's (BookId, IdempotencyKey) de-dupe keyed
/// on the run id; an idempotency pre-check guards the valuation-store mutation (which runs outside the engine)
/// so a re-receive can't double-stock the store.</para>
///
/// <para><b>Subassembly output:</b> a Subassembly produced into stock debits its own INVENTORY_SUBASSEMBLY
/// account (Dr INVENTORY_SUBASSEMBLY / Cr INVENTORY_WIP) — distinct from the open-job WIP it relieves — so it
/// reconciles by part via the valuation store like any other stocked class.</para>
/// </summary>
public interface IProductionReceiptPostingService
{
    /// <summary>
    /// Posts the FG / WIP journal for a received <see cref="ProductionRun"/> (reading its
    /// <see cref="ProductionRun.ReceivedQuantity"/>), when (and only when) CAP-ACCT-FULLGL is enabled. A no-op
    /// while the capability is off, for a non-stocked part, a zero received quantity, or no resolvable cost.
    /// </summary>
    Task PostProductionReceiptAsync(
        int productionRunId, DateOnly entryDate, int receivedByUserId, CancellationToken ct = default);
}

/// <inheritdoc />
public sealed class ProductionReceiptPostingService(
    AppDbContext db,
    IPostingEngine postingEngine,
    ICapabilitySnapshotProvider capabilities,
    ISystemAuditWriter? auditWriter = null,
    IInventoryValuationService? valuation = null) : IProductionReceiptPostingService
{
    private const string FullGlCapability = "CAP-ACCT-FULLGL";

    private const string KeyInventoryRaw = "INVENTORY_RAW";
    private const string KeyInventoryWip = "INVENTORY_WIP";
    private const string KeyInventorySubassembly = "INVENTORY_SUBASSEMBLY";
    private const string KeyInventoryFg = "INVENTORY_FG";

    public async Task PostProductionReceiptAsync(
        int productionRunId, DateOnly entryDate, int receivedByUserId, CancellationToken ct = default)
    {
        if (!capabilities.IsEnabled(FullGlCapability))
            return;

        var run = await db.Set<ProductionRun>()
            .Include(r => r.Part).ThenInclude(p => p!.CurrentCostCalculation)
            .FirstOrDefaultAsync(r => r.Id == productionRunId, ct);
        if (run is null)
        {
            Log.Warning("Production-receipt posting skipped: run {RunId} not found (FULLGL on).", productionRunId);
            return;
        }

        if (!IsStocked(run.Part) || run.ReceivedQuantity <= 0)
            return;

        var debitKey = DebitKeyFor(run.Part);
        var unitStd = ResolveStandardCost(run.Part!);
        if (unitStd is not { } std || std <= 0m)
        {
            Log.Information(
                "Production-receipt GL skipped for run {RunId}: part {PartNumber} has no resolvable standard cost.",
                run.Id, run.Part!.PartNumber);
            return;
        }

        var fgValue = Math.Round(std * run.ReceivedQuantity, 2, MidpointRounding.AwayFromZero);
        if (fgValue <= 0m)
            return;

        var book = await db.Books.AsNoTracking()
            .Where(b => b.IsActive).OrderBy(b => b.Id).FirstOrDefaultAsync(ct);
        if (book is null)
            throw new PostingException(
                "NO_POSTING_BOOK",
                "CAP-ACCT-FULLGL is enabled but no active accounting Book is seeded to post the production receipt into.");

        var idempotencyKey = $"{JournalSource.Inventory}:ProductionRun:{run.Id}:FGRECEIPT";

        // Idempotency guard for the store side-effect (ApplyReceiptAsync runs outside the engine's de-dupe).
        var alreadyPosted = await db.JournalEntries.IgnoreQueryFilters()
            .AnyAsync(e => e.BookId == book.Id && e.IdempotencyKey == idempotencyKey, ct);
        if (alreadyPosted)
            return;

        var partLabel = run.Part?.PartNumber ?? $"part {run.PartId}";
        var desc = $"Production receipt — run {run.RunNumber} (job {run.JobId}) {partLabel} x{run.ReceivedQuantity}";

        var request = new PostingRequest
        {
            BookId = book.Id,
            EntryDate = entryDate,
            Source = JournalSource.Inventory,
            SourceType = "ProductionRun",
            SourceId = run.Id,
            CurrencyId = book.FunctionalCurrencyId,
            Memo = $"Production receipt — run {run.RunNumber}, {partLabel}",
            IdempotencyKey = idempotencyKey,
            Lines =
            [
                new PostingLine { AccountKey = debitKey, Debit = fgValue, Description = desc },
                new PostingLine { AccountKey = KeyInventoryWip, Credit = fgValue, Description = desc },
            ],
        };

        var entry = await postingEngine.PostAsync(request, receivedByUserId, ct);

        // Feed the perpetual FG valuation store at standard cost (joins this transaction).
        if (valuation is not null)
            await valuation.ApplyReceiptAsync(book.Id, run.PartId, run.ReceivedQuantity, fgValue, ct);

        await TryAuditAsync(run, entry, fgValue, receivedByUserId, ct);
    }

    /// <summary>True for perpetual-stocked inventory classes (the ones backed by an INVENTORY_* account).</summary>
    private static bool IsStocked(Part? part) => part?.InventoryClass is
        InventoryClass.Raw or InventoryClass.Component or InventoryClass.Subassembly or InventoryClass.FinishedGood;

    /// <summary>Maps the produced part's <see cref="InventoryClass"/> to the inventory account it capitalizes
    /// into. Finished goods → FG (the common case); a Subassembly maps to WIP (handled as the wash edge above);
    /// raw/component (unusual as a production output) defaults to raw inventory.</summary>
    private static string DebitKeyFor(Part? part) => part?.InventoryClass switch
    {
        InventoryClass.FinishedGood => KeyInventoryFg,
        InventoryClass.Subassembly => KeyInventorySubassembly,
        InventoryClass.Raw or InventoryClass.Component => KeyInventoryRaw,
        _ => KeyInventoryFg,
    };

    /// <summary>Resolved per-unit standard cost (documented read priority, Part.cs):
    /// ManualCostOverride ?? CurrentCostCalculation.ResultAmount ?? null.</summary>
    private static decimal? ResolveStandardCost(Part part) =>
        part.ManualCostOverride ?? part.CurrentCostCalculation?.ResultAmount;

    private async Task TryAuditAsync(
        ProductionRun run, JournalEntry entry, decimal fgValue, int actorUserId, CancellationToken ct)
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
                    productionRunId = run.Id,
                    runNumber = run.RunNumber,
                    jobId = run.JobId,
                    partId = run.PartId,
                    receivedQuantity = run.ReceivedQuantity,
                    fgValue = fgValue.ToString(CultureInfo.InvariantCulture),
                },
                reason = $"Production receipt — run {run.RunNumber} (job {run.JobId}) FG / WIP posting.",
            });

            await auditWriter.WriteAsync(
                action: "GlProductionReceiptPosted",
                userId: actorUserId,
                entityType: nameof(JournalEntry),
                entityId: null,
                details: details,
                ct: ct);
        }
        catch (Exception ex)
        {
            Log.Warning(ex,
                "Production-receipt posting audit write failed for run {RunId} (entry {EntryId}); posting itself is committed.",
                run.Id, entry.Id);
        }
    }
}
