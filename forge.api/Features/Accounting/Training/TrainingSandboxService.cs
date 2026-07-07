using Microsoft.EntityFrameworkCore;

using Forge.Core.Entities.Accounting;
using Forge.Core.Enums.Accounting;
using Forge.Core.Interfaces;
using Forge.Core.Models.Accounting;
using Forge.Data.Context;

namespace Forge.Api.Features.Accounting.Training;

/// <summary>State of the training sandbox, as the UI needs it.</summary>
public record TrainingSandboxState(bool Seeded, int? BookId, int EntryCount);

/// <summary>
/// §5A.4 training sandbox (decisions D1/D2, 2026-07-07): a second, isolated `TRAINING` book seeded
/// with one quarter of realistic history + the five planted errors (P1–P5), posted through the REAL
/// posting engine so every invariant holds. Reset = delete the book's journal rows (permitted by the
/// TRAINING-only DELETE carve-out in the immutability triggers) + reseed.
/// </summary>
public interface ITrainingSandboxService
{
    Task<TrainingSandboxState> GetStateAsync(CancellationToken ct = default);

    /// <summary>Seeds the sandbox if absent; returns its state either way.</summary>
    Task<TrainingSandboxState> EnsureSeededAsync(int actorUserId, CancellationToken ct = default);

    /// <summary>Wipes the sandbox ledger (journal + balances + sequences) and reseeds it.</summary>
    Task<TrainingSandboxState> ResetAsync(int actorUserId, CancellationToken ct = default);
}

/// <inheritdoc />
public sealed class TrainingSandboxService(AppDbContext db, IPostingEngine postingEngine) : ITrainingSandboxService
{
    public const string BookCode = "TRAINING";

    // Teaching chart of accounts (13; design doc §1). Numbers are stable — scenarios reference them.
    private static readonly (string Number, string Name, AccountType Type, NormalBalance Normal, bool Control, ControlAccountType? ControlType)[] Accounts =
    [
        ("10100", "Cash — Operating", AccountType.Asset, NormalBalance.Debit, false, null),
        ("10150", "Cash in Transit", AccountType.Asset, NormalBalance.Debit, false, null),
        ("11000", "Accounts Receivable", AccountType.Asset, NormalBalance.Debit, true, ControlAccountType.AR),
        ("12000", "Prepaid Expenses", AccountType.Asset, NormalBalance.Debit, false, null),
        ("12500", "Inventory — Raw", AccountType.Asset, NormalBalance.Debit, false, null),
        ("12700", "Inventory — Finished Goods", AccountType.Asset, NormalBalance.Debit, false, null),
        ("20000", "Accounts Payable", AccountType.Liability, NormalBalance.Credit, true, ControlAccountType.AP),
        ("22100", "Sales Tax Payable", AccountType.Liability, NormalBalance.Credit, false, null),
        ("30000", "Owner's Equity", AccountType.Equity, NormalBalance.Credit, false, null),
        ("40000", "Sales Revenue", AccountType.Income, NormalBalance.Credit, false, null),
        ("50000", "Cost of Goods Sold", AccountType.Expense, NormalBalance.Debit, false, null),
        ("60100", "Rent Expense", AccountType.Expense, NormalBalance.Debit, false, null),
        ("60300", "Utilities Expense", AccountType.Expense, NormalBalance.Debit, false, null),
    ];

    // Synthetic sub-ledger parties (polymorphic ids — the sandbox has no operational Customers/Vendors).
    private const int CustomerMeridian = 9001;
    private const int VendorCityPower = 9101;

    public async Task<TrainingSandboxState> GetStateAsync(CancellationToken ct = default)
    {
        var book = await db.Books.AsNoTracking().FirstOrDefaultAsync(b => b.Code == BookCode, ct);
        if (book is null) return new TrainingSandboxState(false, null, 0);
        var count = await db.JournalEntries.IgnoreQueryFilters().CountAsync(e => e.BookId == book.Id, ct);
        return new TrainingSandboxState(count > 0, book.Id, count);
    }

    public async Task<TrainingSandboxState> EnsureSeededAsync(int actorUserId, CancellationToken ct = default)
    {
        var state = await GetStateAsync(ct);
        if (state.Seeded) return state;
        var bookId = state.BookId ?? await CreateBookScaffoldAsync(ct);
        await PostQuarterAsync(bookId, actorUserId, ct);
        return await GetStateAsync(ct);
    }

    public async Task<TrainingSandboxState> ResetAsync(int actorUserId, CancellationToken ct = default)
    {
        var book = await db.Books.FirstOrDefaultAsync(b => b.Code == BookCode, ct);
        if (book is not null)
        {
            // DELETE order honours FKs; permitted only because the immutability triggers carve out
            // the TRAINING book (D2). ExecuteDelete bypasses the EF interceptor by design — the
            // interceptor guards tracked-entity saves; the DB trigger remains the last line.
            await db.JournalLines.IgnoreQueryFilters().Where(l => l.BookId == book.Id).ExecuteDeleteAsync(ct);
            await db.JournalEntries.IgnoreQueryFilters().Where(e => e.BookId == book.Id).ExecuteDeleteAsync(ct);
            await db.Set<LedgerBalance>().IgnoreQueryFilters().Where(b => b.BookId == book.Id).ExecuteDeleteAsync(ct);
            await db.Set<AcctNumberSequence>().Where(s => s.BookId == book.Id).ExecuteDeleteAsync(ct);
        }
        return await EnsureSeededAsync(actorUserId, ct);
    }

    private async Task<int> CreateBookScaffoldAsync(CancellationToken ct)
    {
        var currencyId = await db.Currencies.AsNoTracking().OrderBy(c => c.Id).Select(c => c.Id).FirstAsync(ct);
        var book = new Book
        {
            Code = BookCode,
            Name = "Training Sandbox",
            FunctionalCurrencyId = currencyId,
            ReportingTimeZone = "America/Denver",
            RoundingTolerance = 0.01m,
            IsActive = true,
        };
        db.Books.Add(book);
        await db.SaveChangesAsync(ct);

        var fy = new FiscalYear { BookId = book.Id, Name = "FY2026 (training)", StartDate = new DateOnly(2026, 1, 1), EndDate = new DateOnly(2026, 12, 31) };
        db.Set<FiscalYear>().Add(fy);
        await db.SaveChangesAsync(ct);
        for (var m = 1; m <= 12; m++)
        {
            var start = new DateOnly(2026, m, 1);
            db.Set<FiscalPeriod>().Add(new FiscalPeriod
            {
                FiscalYearId = fy.Id,
                PeriodNumber = m,
                Name = start.ToString("MMM yyyy"),
                StartDate = start,
                EndDate = start.AddMonths(1).AddDays(-1),
                Status = FiscalPeriodStatus.Open,
            });
        }
        foreach (var (number, name, type, normal, control, controlType) in Accounts)
        {
            db.GlAccounts.Add(new GlAccount
            {
                BookId = book.Id,
                AccountNumber = number,
                Name = name,
                AccountType = type,
                NormalBalance = normal,
                IsControlAccount = control,
                ControlType = controlType,
                IsPostable = true,
                IsActive = true,
            });
        }
        await db.SaveChangesAsync(ct);
        return book.Id;
    }

    private async Task PostQuarterAsync(int bookId, int actorUserId, CancellationToken ct)
    {
        var currencyId = await db.Books.AsNoTracking().Where(b => b.Id == bookId).Select(b => b.FunctionalCurrencyId).FirstAsync(ct);
        var idByNumber = await db.GlAccounts.AsNoTracking()
            .Where(a => a.BookId == bookId)
            .ToDictionaryAsync(a => a.AccountNumber, a => a.Id, ct);

        var seq = 0;
        Task Post(DateOnly date, string memo, params PostingLine[] lines) =>
            postingEngine.PostAsync(new PostingRequest
            {
                BookId = bookId,
                EntryDate = date,
                Source = JournalSource.Manual,
                CurrencyId = currencyId,
                Memo = memo,
                Lines = lines,
                // Manual normally carries no key; the seeder supplies one so a crashed half-seed
                // re-run never double-posts. (The engine permits keys on Manual; it requires them
                // only for non-Manual.)
                IdempotencyKey = $"TRAINING:SEED:{++seq}",
            }, actorUserId, ct);

        PostingLine Dr(string acct, decimal amt, string? desc = null, SubledgerPartyType? pt = null, int? pid = null) =>
            new() { GlAccountId = idByNumber[acct], Debit = amt, Description = desc, PartyType = pt, PartyId = pid };
        PostingLine Cr(string acct, decimal amt, string? desc = null, SubledgerPartyType? pt = null, int? pid = null) =>
            new() { GlAccountId = idByNumber[acct], Credit = amt, Description = desc, PartyType = pt, PartyId = pid };

        // ── Opening (the QB "Opening Balance Equity" anti-pattern done RIGHT: one balanced journal) ──
        await Post(new(2026, 4, 1), "Opening balances — quarter start",
            Dr("10100", 25_000m, "Opening cash"), Dr("12500", 8_000m, "Opening raw stock"),
            Dr("12700", 4_000m, "Opening finished goods"), Cr("30000", 37_000m, "Opening equity"));

        for (var m = 4; m <= 6; m++)
        {
            await Post(new(2026, m, 1), $"Rent — {new DateOnly(2026, m, 1):MMMM}", Dr("60100", 2_200m, "Monthly rent"), Cr("10100", 2_200m));

            // Utilities: April's is P1 (miscoded to Rent); June's is duplicated (P2).
            if (m == 4)
                await Post(new(2026, 4, 12), "April power bill — City Power", Dr("60100", 842.17m, "Power"), Cr("10100", 842.17m)); // P1: WRONG account (Rent)
            else
                await Post(new(2026, m, 12), $"Utilities — City Power ({new DateOnly(2026, m, 1):MMM})", Dr("60300", 842.17m, "Power"), Cr("10100", 842.17m));

            await Post(new(2026, m, 8), "Cash sale — walk-in", Dr("10100", 3_150m), Cr("40000", 3_000m, "Sale"), Cr("22100", 150m, "Sales tax"));
            await Post(new(2026, m, 20), "Invoice — Meridian Tools",
                Dr("11000", 5_250m, "INV", SubledgerPartyType.Customer, CustomerMeridian), Cr("40000", 5_000m), Cr("22100", 250m));
            await Post(new(2026, m, 28), "Receipt — Meridian Tools",
                Dr("10100", 5_250m), Cr("11000", 5_250m, "Payment", SubledgerPartyType.Customer, CustomerMeridian));

            await Post(new(2026, m, 10), "Raw stock purchase — Apex Metals",
                Dr("12500", 2_400m), Cr("20000", 2_400m, "Bill", SubledgerPartyType.Vendor, VendorCityPower));
            await Post(new(2026, m, 24), "Bill payment — Apex Metals",
                Dr("20000", 2_400m, "Payment", SubledgerPartyType.Vendor, VendorCityPower), Cr("10100", 2_400m));

            await Post(new(2026, m, 25), $"Payroll — {new DateOnly(2026, m, 1):MMMM}", Dr("50000", 6_500m, "Shop labour"), Cr("10100", 6_500m));
        }

        // ── Plants P2–P5 ──
        await Post(new(2026, 6, 15), "Utilities — City Power (Jun)", Dr("60300", 842.17m, "Power"), Cr("10100", 842.17m)); // P2: duplicate of Jun 12
        await Post(new(2026, 5, 30), "Customer check deposit", Dr("10100", 1_900m), Cr("10150", 1_900m, "In transit"));      // P3: later bounces (NSF)
        await Post(new(2026, 6, 18), "Adjust AR — manual",
            Dr("11000", 150m, "adj", SubledgerPartyType.Customer, CustomerMeridian), Cr("40000", 150m));                     // P4: hand-post to control
        await Post(new(2026, 6, 22), "adjust to match bank", Dr("60300", 75m), Cr("10100", 75m));                            // P5: no narration on lines

        // ── Quarter-end ──
        await Post(new(2026, 6, 30), "COGS true-up — June shipment", Dr("50000", 1_800m), Cr("12700", 1_800m));
        await Post(new(2026, 7, 2), "Sales tax remittance — Q2", Dr("22100", 1_200m), Cr("10100", 1_200m));
    }
}
