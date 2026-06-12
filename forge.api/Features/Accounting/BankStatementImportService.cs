using Microsoft.EntityFrameworkCore;

using Forge.Core.Entities.Accounting;
using Forge.Core.Enums.Accounting;
using Forge.Core.Interfaces;
using Forge.Core.Models.Accounting;
using Forge.Core.Settings;
using Forge.Data.Context;
using Serilog;

namespace Forge.Api.Features.Accounting;

/// <summary>
/// ⚡ BANK-001 — bank statement import + auto-match staging:
/// <list type="bullet">
///   <item><b>Import</b> parses OFX/CSV, dedupes per (cash account, FITID) — re-imports and
///         overlapping exports insert nothing twice — then auto-matches.</item>
///   <item><b>Auto-match</b> proposes a journal line only when EXACTLY ONE Posted cash line fits
///         (same signed amount, entry date within the configured window, not already claimed by
///         another statement line). Ambiguity stays Unmatched — a human resolves it; the matcher
///         never guesses.</item>
///   <item><b>Confirm</b> is the settlement attestation: it also flips the line's
///         <c>BankReconciliationItem.IsCleared</c> in any open Draft reconciliation of the same
///         cash account — this is how "Succeeded = submission accepted" payments get their actual
///         settlement confirmed (§7 cash-in-transit note).</item>
/// </list>
/// </summary>
public interface IBankStatementImportService
{
    Task<ImportBankStatementResultModel> ImportAsync(int bookId, int cashGlAccountId, string fileName, string contents, int userId, CancellationToken ct = default);
    Task<IReadOnlyList<BankStatementImportModel>> ListImportsAsync(int? cashGlAccountId, CancellationToken ct = default);
    Task<IReadOnlyList<BankStatementLineModel>> GetLinesAsync(int importId, string? status, CancellationToken ct = default);
    Task<int> AutoMatchAsync(int importId, CancellationToken ct = default);
    Task<BankStatementLineModel> ConfirmAsync(long lineId, int userId, CancellationToken ct = default);
    Task<BankStatementLineModel> ManualMatchAsync(long lineId, long journalLineId, int userId, CancellationToken ct = default);
    Task<BankStatementLineModel> IgnoreAsync(long lineId, CancellationToken ct = default);
    Task<BankStatementLineModel> UnmatchAsync(long lineId, CancellationToken ct = default);
}

/// <inheritdoc />
public sealed class BankStatementImportService(
    AppDbContext db,
    ISettingsService settings,
    IClock clock) : IBankStatementImportService
{
    public async Task<ImportBankStatementResultModel> ImportAsync(
        int bookId, int cashGlAccountId, string fileName, string contents, int userId, CancellationToken ct = default)
    {
        var account = await db.GlAccounts.AsNoTracking()
            .FirstOrDefaultAsync(a => a.Id == cashGlAccountId && a.BookId == bookId, ct)
            ?? throw new KeyNotFoundException($"Cash GL account {cashGlAccountId} not found in book {bookId}");

        var isOfx = BankStatementParser.LooksLikeOfx(contents);
        var parsed = isOfx ? BankStatementParser.ParseOfx(contents) : BankStatementParser.ParseCsv(contents);
        if (parsed.Count == 0)
            throw new InvalidOperationException(
                "No transactions found in the file — check the format (OFX, or CSV with Date/Amount headers).");

        var fitids = parsed.Select(p => p.Fitid).ToList();
        var existing = (await db.BankStatementLines
            .Where(l => l.CashGlAccountId == cashGlAccountId && fitids.Contains(l.Fitid))
            .Select(l => l.Fitid)
            .ToListAsync(ct)).ToHashSet();

        var import = new BankStatementImport
        {
            BookId = bookId,
            CashGlAccountId = cashGlAccountId,
            FileName = fileName,
            Format = isOfx ? BankStatementFormat.Ofx : BankStatementFormat.Csv,
            ImportedByUserId = userId,
        };

        foreach (var p in parsed)
        {
            if (existing.Contains(p.Fitid))
            {
                import.DuplicateCount++;
                continue;
            }
            existing.Add(p.Fitid); // in-file duplicates dedupe too
            import.Lines.Add(new BankStatementLine
            {
                CashGlAccountId = cashGlAccountId,
                PostedDate = p.PostedDate,
                Amount = p.Amount,
                Description = p.Description.Length > 256 ? p.Description[..256] : p.Description,
                Fitid = p.Fitid,
            });
        }
        import.LineCount = import.Lines.Count;

        db.BankStatementImports.Add(import);
        await db.SaveChangesAsync(ct);

        var suggested = await AutoMatchAsync(import.Id, ct);

        Log.Information(
            "Bank statement {FileName} imported for account {AccountNumber}: {Imported} new, {Duplicates} duplicate, {Suggested} auto-suggested.",
            fileName, account.AccountNumber, import.LineCount, import.DuplicateCount, suggested);

        return new ImportBankStatementResultModel(import.Id, import.LineCount, import.DuplicateCount, suggested);
    }

    public async Task<IReadOnlyList<BankStatementImportModel>> ListImportsAsync(
        int? cashGlAccountId, CancellationToken ct = default)
    {
        var query = db.BankStatementImports.AsNoTracking().Include(i => i.Lines).AsQueryable();
        if (cashGlAccountId is int id)
            query = query.Where(i => i.CashGlAccountId == id);

        var imports = await query.OrderByDescending(i => i.Id).ToListAsync(ct);
        return imports.Select(i => new BankStatementImportModel(
            i.Id, i.CashGlAccountId, i.FileName, i.Format.ToString(), i.LineCount, i.DuplicateCount,
            i.Lines.Count(l => l.MatchStatus == BankStatementMatchStatus.Unmatched),
            i.Lines.Count(l => l.MatchStatus == BankStatementMatchStatus.Suggested),
            i.Lines.Count(l => l.MatchStatus == BankStatementMatchStatus.Confirmed),
            i.Lines.Count(l => l.MatchStatus == BankStatementMatchStatus.Ignored),
            i.CreatedAt)).ToList();
    }

    public async Task<IReadOnlyList<BankStatementLineModel>> GetLinesAsync(
        int importId, string? status, CancellationToken ct = default)
    {
        var query = db.BankStatementLines.AsNoTracking()
            .Include(l => l.MatchedJournalLine).ThenInclude(jl => jl!.JournalEntry)
            .Where(l => l.BankStatementImportId == importId);
        if (Enum.TryParse<BankStatementMatchStatus>(status, ignoreCase: true, out var st))
            query = query.Where(l => l.MatchStatus == st);

        var lines = await query.OrderBy(l => l.PostedDate).ThenBy(l => l.Id).ToListAsync(ct);
        return lines.Select(l => ToModel(l)).ToList();
    }

    public async Task<int> AutoMatchAsync(int importId, CancellationToken ct = default)
    {
        var import = await db.BankStatementImports
            .Include(i => i.Lines)
            .FirstOrDefaultAsync(i => i.Id == importId, ct)
            ?? throw new KeyNotFoundException($"Statement import {importId} not found");

        var windowDays = int.TryParse(
            await settings.GetStringAsync(BankingSettings.StatementMatchWindowDaysKey, ct), out var w) ? w : 5;

        var unmatched = import.Lines
            .Where(l => l.MatchStatus == BankStatementMatchStatus.Unmatched)
            .ToList();
        if (unmatched.Count == 0)
            return 0;

        // Candidate pool: Posted cash lines in the date envelope of the whole import (one query).
        var minDate = unmatched.Min(l => l.PostedDate).AddDays(-windowDays);
        var maxDate = unmatched.Max(l => l.PostedDate).AddDays(windowDays);

        var candidates = await db.JournalLines.AsNoTracking()
            .Where(jl => jl.GlAccountId == import.CashGlAccountId
                && jl.JournalEntry.Status == JournalEntryStatus.Posted
                && jl.JournalEntry.EntryDate >= minDate
                && jl.JournalEntry.EntryDate <= maxDate)
            .Select(jl => new
            {
                jl.Id,
                jl.Debit,
                jl.FunctionalAmount,
                jl.JournalEntry.EntryDate,
            })
            .ToListAsync(ct);

        // Journal lines already claimed by ANY live (suggested/confirmed) statement line.
        var claimed = (await db.BankStatementLines
            .Where(l => l.CashGlAccountId == import.CashGlAccountId
                && l.MatchedJournalLineId != null
                && (l.MatchStatus == BankStatementMatchStatus.Suggested
                    || l.MatchStatus == BankStatementMatchStatus.Confirmed))
            .Select(l => l.MatchedJournalLineId!.Value)
            .ToListAsync(ct)).ToHashSet();

        var suggested = 0;
        foreach (var line in unmatched.OrderBy(l => l.PostedDate).ThenBy(l => l.Id))
        {
            // Bank + = money in = cash DEBIT; bank − = money out = cash CREDIT.
            var fits = candidates.Where(c =>
                    !claimed.Contains(c.Id)
                    && (c.Debit > 0m ? c.FunctionalAmount : -c.FunctionalAmount) == line.Amount
                    && Math.Abs(c.EntryDate.DayNumber - line.PostedDate.DayNumber) <= windowDays)
                .ToList();

            // Exactly one candidate or no proposal — the matcher never guesses between siblings.
            if (fits.Count == 1)
            {
                line.MatchStatus = BankStatementMatchStatus.Suggested;
                line.MatchedJournalLineId = fits[0].Id;
                claimed.Add(fits[0].Id);
                suggested++;
            }
        }

        await db.SaveChangesAsync(ct);
        return suggested;
    }

    public async Task<BankStatementLineModel> ConfirmAsync(long lineId, int userId, CancellationToken ct = default)
    {
        var line = await FindLineAsync(lineId, ct);
        if (line.MatchStatus != BankStatementMatchStatus.Suggested || line.MatchedJournalLineId is null)
            throw new InvalidOperationException("Only a suggested match can be confirmed (manually match first).");

        line.MatchStatus = BankStatementMatchStatus.Confirmed;
        line.ConfirmedByUserId = userId;
        line.ConfirmedAt = clock.UtcNow;

        await SetClearedInOpenReconciliationAsync(line, ct);

        await db.SaveChangesAsync(ct);
        return ToModel(line);
    }

    public async Task<BankStatementLineModel> ManualMatchAsync(
        long lineId, long journalLineId, int userId, CancellationToken ct = default)
    {
        var line = await FindLineAsync(lineId, ct);
        if (line.MatchStatus == BankStatementMatchStatus.Confirmed)
            throw new InvalidOperationException("Line is already confirmed — unmatch it first.");

        var journalLine = await db.JournalLines.AsNoTracking()
            .Include(jl => jl.JournalEntry)
            .FirstOrDefaultAsync(jl => jl.Id == journalLineId, ct)
            ?? throw new KeyNotFoundException($"Journal line {journalLineId} not found");

        if (journalLine.GlAccountId != line.CashGlAccountId)
            throw new InvalidOperationException("The journal line is not on this statement's cash account.");
        if (journalLine.JournalEntry.Status != JournalEntryStatus.Posted)
            throw new InvalidOperationException("Only a Posted journal line can be matched.");

        var claimed = await db.BankStatementLines.AnyAsync(l =>
            l.Id != line.Id
            && l.MatchedJournalLineId == journalLineId
            && (l.MatchStatus == BankStatementMatchStatus.Suggested
                || l.MatchStatus == BankStatementMatchStatus.Confirmed), ct);
        if (claimed)
            throw new InvalidOperationException("That journal line is already matched to another statement line.");

        line.MatchedJournalLineId = journalLineId;
        line.MatchStatus = BankStatementMatchStatus.Confirmed;
        line.ConfirmedByUserId = userId;
        line.ConfirmedAt = clock.UtcNow;

        await SetClearedInOpenReconciliationAsync(line, ct);

        await db.SaveChangesAsync(ct);
        return ToModel(line, journalLine);
    }

    public async Task<BankStatementLineModel> IgnoreAsync(long lineId, CancellationToken ct = default)
    {
        var line = await FindLineAsync(lineId, ct);
        if (line.MatchStatus == BankStatementMatchStatus.Confirmed)
            throw new InvalidOperationException("A confirmed line cannot be ignored — unmatch it first.");

        line.MatchStatus = BankStatementMatchStatus.Ignored;
        line.MatchedJournalLineId = null;
        await db.SaveChangesAsync(ct);
        return ToModel(line);
    }

    public async Task<BankStatementLineModel> UnmatchAsync(long lineId, CancellationToken ct = default)
    {
        var line = await FindLineAsync(lineId, ct);

        // Undo the reconciliation clearing the confirm applied (only for a still-Draft rec).
        if (line.MatchStatus == BankStatementMatchStatus.Confirmed && line.MatchedJournalLineId is long jlId)
        {
            var item = await db.Set<BankReconciliationItem>()
                .Include(i => i.BankReconciliation)
                .Where(i => i.JournalLineId == jlId
                    && i.BankReconciliation.Status == BankReconciliationStatus.Draft
                    && i.BankReconciliation.CashGlAccountId == line.CashGlAccountId)
                .FirstOrDefaultAsync(ct);
            if (item is not null)
                item.IsCleared = false;
        }

        line.MatchStatus = BankStatementMatchStatus.Unmatched;
        line.MatchedJournalLineId = null;
        line.ConfirmedByUserId = null;
        line.ConfirmedAt = null;
        await db.SaveChangesAsync(ct);
        return ToModel(line);
    }

    /// <summary>
    /// The BANK-001 ↔ bank-rec seam: a confirmed statement match clears the journal line in any
    /// open Draft reconciliation of the same cash account (a finalized rec is immutable history).
    /// </summary>
    private async Task SetClearedInOpenReconciliationAsync(BankStatementLine line, CancellationToken ct)
    {
        if (line.MatchedJournalLineId is not long jlId)
            return;

        var item = await db.Set<BankReconciliationItem>()
            .Include(i => i.BankReconciliation)
            .Where(i => i.JournalLineId == jlId
                && i.BankReconciliation.Status == BankReconciliationStatus.Draft
                && i.BankReconciliation.CashGlAccountId == line.CashGlAccountId)
            .FirstOrDefaultAsync(ct);
        if (item is not null)
            item.IsCleared = true;
    }

    private async Task<BankStatementLine> FindLineAsync(long lineId, CancellationToken ct)
        => await db.BankStatementLines
            .Include(l => l.MatchedJournalLine).ThenInclude(jl => jl!.JournalEntry)
            .FirstOrDefaultAsync(l => l.Id == lineId, ct)
            ?? throw new KeyNotFoundException($"Statement line {lineId} not found");

    private BankStatementLineModel ToModel(BankStatementLine line, JournalLine? journalLine = null)
    {
        var jl = journalLine ?? line.MatchedJournalLine;
        return new BankStatementLineModel(
            line.Id,
            line.PostedDate.ToString("yyyy-MM-dd"),
            line.Amount,
            line.Description,
            line.MatchStatus.ToString(),
            line.MatchedJournalLineId,
            jl?.JournalEntry?.EntryNumber,
            jl?.JournalEntry?.EntryDate.ToString("yyyy-MM-dd"),
            jl?.JournalEntry?.Memo,
            line.ConfirmedAt);
    }
}
