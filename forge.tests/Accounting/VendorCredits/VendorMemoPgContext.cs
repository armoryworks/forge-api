using System.Security.Claims;

using MediatR;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Moq;

using Forge.Api.Features.Accounting;
using Forge.Api.Features.PaymentRuns;
using Forge.Api.Features.VendorCredits;
using Forge.Api.Features.VendorPayments;
using Forge.Api.Features.VendorReturns;
using Forge.Core.Entities;
using Forge.Core.Entities.Accounting;
using Forge.Core.Enums;
using Forge.Core.Enums.Accounting;
using Forge.Core.Models;
using Forge.Data.Context;
using Forge.Data.Repositories;
using Forge.Integrations;
using Forge.Tests.Helpers;

namespace Forge.Tests.Accounting.VendorCredits;

/// <summary>
/// A FULLGL book on the shared Postgres with the accounts vendor memos, returns to vendor and payment runs post to,
/// plus the handlers under test wired the way the API wires them (one context per request, a mediator that dispatches
/// the handlers these call).
/// </summary>
internal sealed class VendorMemoPgContext(PostgresFixture fixture)
{
    public const int BookId = 1;
    public const int UsdId = 1;
    public const int EurId = 2;
    public const int CashId = 300;
    public const int ApId = 301;
    public const int ExpenseId = 302;
    public const int PpvId = 303;
    public const int GrniId = 304;
    public const int InventoryRawId = 305;
    public const int ClearingId = 306;
    public const int FxGainId = 307;
    public const int FxLossId = 308;
    public const int DiscountsId = 309;
    public const int FreightInId = 310;
    public const int RevaluationId = 311;

    public static readonly DateTimeOffset March = new(2026, 3, 10, 12, 0, 0, TimeSpan.Zero);

    public int UserId { get; private set; }

    public AppDbContext Db()
    {
        var db = fixture.CreateContext();
        db.CurrentUserId = UserId;
        return db;
    }

    public static StubCapabilitySnapshotProvider Caps() => new("CAP-ACCT-FULLGL", "CAP-P2P-PO", "CAP-P2P-BILL", "CAP-QC-NCR");

    public static ForgeGlPostingEngine Engine(AppDbContext db)
        => new(db, new AccountDeterminationResolver(db), new AcctNumberSequenceAllocator(db), new SystemClock());

    public static VendorCreditMemoPostingService MemoPosting(AppDbContext db)
        => new(db, Engine(db), Caps(), new InventoryValuationService(db));

    public static VendorBillApPostingService BillPosting(AppDbContext db) => new(db, Engine(db), Caps());

    public IHttpContextAccessor Http() => new HttpContextAccessor
    {
        HttpContext = new DefaultHttpContext
        {
            User = new ClaimsPrincipal(new ClaimsIdentity(
                [new Claim(ClaimTypes.NameIdentifier, UserId.ToString()), new Claim(ClaimTypes.Role, "Admin")], "test")),
        },
    };

    public CreateVendorPaymentHandler PaymentHandler(AppDbContext db)
        => new(new VendorPaymentRepository(db), new VendorRepository(db), new VendorBillRepository(db), db,
            new VendorPaymentCashPostingService(db, Engine(db), Caps()), Http(), creditPosting: MemoPosting(db));

    public PaymentRunExecutor Executor(AppDbContext db, ISender sender) => new(db, sender, new SystemClock(), MemoPosting(db));

    /// <summary>A mediator over <paramref name="db"/> that dispatches the memo, return and run handlers, and vendor payments.</summary>
    public Mock<IMediator> Mediator(AppDbContext db, Func<CreateVendorPaymentCommand, Task<VendorPaymentListItemModel>>? pay = null)
    {
        var mediator = new Mock<IMediator>();
        mediator.Setup(m => m.Send(It.IsAny<GetVendorCreditMemoByIdQuery>(), It.IsAny<CancellationToken>()))
            .Returns<IRequest<VendorCreditMemoDetailResponseModel>, CancellationToken>((q, ct) =>
                new GetVendorCreditMemoByIdHandler(db).Handle((GetVendorCreditMemoByIdQuery)q, ct));
        mediator.Setup(m => m.Send(It.IsAny<GetVendorReturnByIdQuery>(), It.IsAny<CancellationToken>()))
            .Returns<IRequest<VendorReturnDetailResponseModel>, CancellationToken>((q, ct) =>
                new GetVendorReturnByIdHandler(db).Handle((GetVendorReturnByIdQuery)q, ct));
        mediator.Setup(m => m.Send(It.IsAny<GetPaymentRunByIdQuery>(), It.IsAny<CancellationToken>()))
            .Returns<IRequest<PaymentRunDetailResponseModel>, CancellationToken>((q, ct) =>
                new GetPaymentRunByIdHandler(db).Handle((GetPaymentRunByIdQuery)q, ct));
        mediator.Setup(m => m.Send(It.IsAny<CreateVendorCreditMemoCommand>(), It.IsAny<CancellationToken>()))
            .Returns<IRequest<VendorCreditMemoDetailResponseModel>, CancellationToken>((c, ct) =>
                new CreateVendorCreditMemoHandler(db, new SystemClock(), mediator.Object).Handle((CreateVendorCreditMemoCommand)c, ct));
        mediator.Setup(m => m.Send(It.IsAny<IssueVendorCreditMemoCommand>(), It.IsAny<CancellationToken>()))
            .Returns<IRequest<VendorCreditMemoDetailResponseModel>, CancellationToken>((c, ct) =>
                new IssueVendorCreditMemoHandler(db, Issuer(db), mediator.Object).Handle((IssueVendorCreditMemoCommand)c, ct));
        mediator.Setup(m => m.Send(It.IsAny<CreateVendorPaymentCommand>(), It.IsAny<CancellationToken>()))
            .Returns<IRequest<VendorPaymentListItemModel>, CancellationToken>((c, ct) =>
                pay is null ? PaymentHandler(db).Handle((CreateVendorPaymentCommand)c, ct) : pay((CreateVendorPaymentCommand)c));
        return mediator;
    }

    public static VendorCreditMemoIssuer Issuer(AppDbContext db) => new(db, MemoPosting(db), new SystemClock());

    public async Task<VendorCreditMemoDetailResponseModel> CreateMemoAsync(CreateVendorCreditMemoRequestModel request)
    {
        await using var db = Db();
        return await new CreateVendorCreditMemoHandler(db, new SystemClock(), Mediator(db).Object)
            .Handle(new CreateVendorCreditMemoCommand(request), CancellationToken.None);
    }

    public async Task<VendorCreditMemoDetailResponseModel> IssueAsync(int memoId)
    {
        await using var db = Db();
        return await new IssueVendorCreditMemoHandler(db, Issuer(db), Mediator(db).Object)
            .Handle(new IssueVendorCreditMemoCommand(memoId), CancellationToken.None);
    }

    public async Task ResetAsync()
    {
        await using (var db = fixture.CreateContext())
        {
            await db.Database.ExecuteSqlRawAsync(@"
DO $$
DECLARE r RECORD;
BEGIN
  FOR r IN (SELECT tablename FROM pg_tables
            WHERE schemaname = 'public' AND tablename <> '__EFMigrationsHistory') LOOP
    EXECUTE 'TRUNCATE TABLE ' || quote_ident(r.tablename) || ' RESTART IDENTITY CASCADE';
  END LOOP;
END $$;");
        }

        await using var seed = fixture.CreateContext();
        var user = new ApplicationUser { UserName = "ap@forge.local", Email = "ap@forge.local", FirstName = "Ap", LastName = "Clerk", IsActive = true };
        seed.Add(user);
        seed.Set<Currency>().AddRange(
            new Currency { Id = UsdId, Code = "USD", Name = "US Dollar", Symbol = "$", IsBaseCurrency = true, IsActive = true },
            new Currency { Id = EurId, Code = "EUR", Name = "Euro", Symbol = "€", IsActive = true });
        await seed.SaveChangesAsync();
        UserId = user.Id;

        seed.Set<Book>().Add(new Book
        {
            Id = BookId, Code = "MAIN", Name = "Main", FunctionalCurrencyId = UsdId,
            ReportingTimeZone = "UTC", RoundingTolerance = 0.01m, IsActive = true,
        });
        await seed.SaveChangesAsync();
        seed.Set<FiscalYear>().Add(new FiscalYear
        {
            Id = 10, BookId = BookId, Name = "FY2026",
            StartDate = new DateOnly(2026, 1, 1), EndDate = new DateOnly(2026, 12, 31), Status = FiscalYearStatus.Open,
        });
        await seed.SaveChangesAsync();
        seed.Set<FiscalPeriod>().Add(new FiscalPeriod
        {
            Id = 1000, FiscalYearId = 10, PeriodNumber = 1, Name = "FY2026",
            StartDate = new DateOnly(2026, 1, 1), EndDate = new DateOnly(2026, 12, 31), Status = FiscalPeriodStatus.Open,
        });

        var accounts = new (int Id, string Number, string Name, AccountType Type, NormalBalance Normal, ControlAccountType? Control, string Key)[]
        {
            (CashId, "10100", "Cash", AccountType.Asset, NormalBalance.Debit, null, "CASH"),
            (ClearingId, "12600", "Vendor Returns Clearing", AccountType.Asset, NormalBalance.Debit, null, "VENDOR_RETURN_CLEARING"),
            (InventoryRawId, "13100", "Inventory Raw", AccountType.Asset, NormalBalance.Debit, ControlAccountType.Inventory, "INVENTORY_RAW"),
            (RevaluationId, "19900", "FX Revaluation", AccountType.Asset, NormalBalance.Debit, null, "FX_REVALUATION"),
            (ApId, "20000", "Accounts Payable", AccountType.Liability, NormalBalance.Credit, ControlAccountType.AP, "AP_CONTROL"),
            (GrniId, "21000", "GRNI", AccountType.Liability, NormalBalance.Credit, null, "GRNI"),
            (FreightInId, "50100", "Freight-In", AccountType.Expense, NormalBalance.Debit, null, "FREIGHT_IN"),
            (DiscountsId, "50500", "Purchase Discounts", AccountType.Expense, NormalBalance.Credit, null, "PURCHASE_DISCOUNTS"),
            (PpvId, "51000", "Purchase Price Variance", AccountType.Expense, NormalBalance.Debit, null, "PURCHASE_PRICE_VARIANCE"),
            (ExpenseId, "60000", "G&A", AccountType.Expense, NormalBalance.Debit, null, "OPERATING_EXPENSE"),
            (FxGainId, "90000", "FX Gain", AccountType.Income, NormalBalance.Credit, null, "FX_GAIN"),
            (FxLossId, "90100", "FX Loss", AccountType.Expense, NormalBalance.Debit, null, "FX_LOSS"),
        };
        foreach (var a in accounts)
        {
            seed.Set<GlAccount>().Add(new GlAccount
            {
                Id = a.Id, BookId = BookId, AccountNumber = a.Number, Name = a.Name, AccountType = a.Type, NormalBalance = a.Normal,
                IsControlAccount = a.Control is not null, ControlType = a.Control, IsPostable = true, IsActive = true,
            });
            seed.Set<AccountDeterminationRule>().Add(new AccountDeterminationRule { BookId = BookId, Key = a.Key, GlAccountId = a.Id });
        }
        await seed.SaveChangesAsync();
        await seed.Database.ExecuteSqlRawAsync(@"
DO $$
DECLARE r RECORD;
BEGIN
  FOR r IN (SELECT c.table_name, c.column_name, pg_get_serial_sequence(quote_ident(c.table_name), c.column_name) AS seq
            FROM information_schema.columns c
            WHERE c.table_schema = 'public' AND c.is_identity = 'YES') LOOP
    IF r.seq IS NOT NULL THEN
      EXECUTE format('SELECT setval(%L, GREATEST((SELECT COALESCE(MAX(%I), 0) FROM %I), 1), (SELECT COUNT(*) > 0 FROM %I))',
        r.seq, r.column_name, r.table_name, r.table_name);
    END IF;
  END LOOP;
END $$;");
    }

    public async Task<int> VendorAsync(string name)
    {
        await using var db = Db();
        var vendor = new Vendor { CompanyName = name, IsActive = true };
        db.Vendors.Add(vendor);
        await db.SaveChangesAsync();
        return vendor.Id;
    }

    /// <summary>An approved standalone bill posted through the real AP posting (journal and open item).</summary>
    public async Task<VendorBill> PostedBillAsync(
        int vendorId, decimal quantity, decimal unitPrice, string number, int currencyId = UsdId, decimal fxRate = 1m,
        decimal discountPercent = 0m)
    {
        await using var db = Db();
        var bill = new VendorBill
        {
            BillNumber = number, VendorId = vendorId, CurrencyId = currencyId, FxRate = fxRate, Status = VendorBillStatus.Approved,
            BillDate = March, DueDate = March.AddDays(30),
            DiscountPercent = discountPercent, DiscountDueDate = discountPercent > 0m ? March.AddDays(10) : null,
            Lines = [new VendorBillLine { Description = "Steel", Quantity = quantity, UnitPrice = unitPrice, LineNumber = 1, AccountDeterminationKey = "OPERATING_EXPENSE" }],
        };
        db.VendorBills.Add(bill);
        await db.SaveChangesAsync();
        await BillPosting(db).PostVendorBillApprovedAsync(bill.Id, UserId);
        return bill;
    }

    public async Task<Dictionary<int, decimal>> NetByAccountAsync()
    {
        await using var db = Db();
        return (await db.JournalLines.AsNoTracking().ToListAsync())
            .GroupBy(l => l.GlAccountId)
            .ToDictionary(g => g.Key, g => g.Sum(l => l.Debit > 0m ? l.FunctionalAmount : -l.FunctionalAmount));
    }

    public async Task<decimal> SignedApOpenItemsAsync()
    {
        await using var db = Db();
        return (await db.ApOpenItems.AsNoTracking().Where(i => i.Status != OpenItemStatus.Voided).ToListAsync())
            .Sum(i => i.OpenFunctionalAmount);
    }

    public async Task<VendorBill> BillAsync(int billId)
    {
        await using var db = Db();
        return await db.VendorBills.AsNoTracking()
            .Include(b => b.Lines).Include(b => b.PaymentApplications).Include(b => b.CreditApplications)
            .SingleAsync(b => b.Id == billId);
    }
}
