using System.Security.Claims;

using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

using Forge.Api.Features.Accounting;
using Forge.Api.Features.Accounting.Sod;
using Forge.Core.Entities;
using Forge.Core.Entities.Accounting;
using Forge.Core.Enums.Accounting;
using Forge.Core.Interfaces;
using Forge.Core.Models.Accounting;
using Forge.Data.Context;
using Forge.Integrations;
using Forge.Tests.Helpers;

namespace Forge.Tests.Accounting;

/// <summary>
/// §5.5 opening-balances hard-gate (<see cref="GlCapabilityGate"/>) + §5.7 SoD
/// enforced at the <see cref="ForgeGlPostingEngine"/> boundary when an
/// authorizer is injected. Both stay dark (CAP-ACCT-FULLGL OFF); these are
/// logic-only / seam tests.
/// </summary>
public class GlCapabilityGateAndBoundaryTests
{
    private const int BookId = 1;
    private const int UsdId = 1;
    private const int FiscalYearId = 10;
    private const int CashId = 100;
    private const int RevenueId = 101;
    private const int OpenPeriodId = 1000;

    private sealed class FakeAllocator : IAcctNumberSequenceAllocator
    {
        private long _next = 1;
        public Task<long> AllocateNextAsync(int bookId, int fiscalYearId, CancellationToken ct = default)
            => Task.FromResult(_next++);
    }

    private static async Task<AppDbContext> SeedAsync()
    {
        var db = TestDbContextFactory.Create();
        db.Set<Currency>().Add(new Currency { Id = UsdId, Code = "USD", Name = "US Dollar", Symbol = "$" });
        db.Set<Book>().Add(new Book
        {
            Id = BookId, Code = "MAIN", Name = "Main", FunctionalCurrencyId = UsdId,
            ReportingTimeZone = "America/New_York", RoundingTolerance = 0.01m, IsActive = true,
        });
        db.Set<FiscalYear>().Add(new FiscalYear
        {
            Id = FiscalYearId, BookId = BookId, Name = "FY2026",
            StartDate = new DateOnly(2026, 1, 1), EndDate = new DateOnly(2026, 12, 31),
            Status = FiscalYearStatus.Open,
        });
        db.Set<FiscalPeriod>().Add(new FiscalPeriod
        {
            Id = OpenPeriodId, FiscalYearId = FiscalYearId, PeriodNumber = 1, Name = "Jan 2026",
            StartDate = new DateOnly(2026, 1, 1), EndDate = new DateOnly(2026, 1, 31),
            Status = FiscalPeriodStatus.Open,
        });
        db.Set<GlAccount>().AddRange(
            new GlAccount
            {
                Id = CashId, BookId = BookId, AccountNumber = "1000", Name = "Cash",
                AccountType = AccountType.Asset, NormalBalance = NormalBalance.Debit,
                IsPostable = true, IsActive = true,
            },
            new GlAccount
            {
                Id = RevenueId, BookId = BookId, AccountNumber = "4000", Name = "Revenue",
                AccountType = AccountType.Income, NormalBalance = NormalBalance.Credit,
                IsPostable = true, IsActive = true,
            });
        await db.SaveChangesAsync();
        return db;
    }

    private static IGlBoundaryAuthorizer AuthorizerForRoles(int userId, params string[] roles)
    {
        var claims = new List<Claim> { new(ClaimTypes.NameIdentifier, userId.ToString()) };
        claims.AddRange(roles.Select(r => new Claim(ClaimTypes.Role, r)));
        var principal = new ClaimsPrincipal(new ClaimsIdentity(claims, "Test"));
        var accessor = new Mock<IHttpContextAccessor>();
        accessor.Setup(a => a.HttpContext).Returns(new DefaultHttpContext { User = principal });
        return new GlBoundaryAuthorizer(
            new CurrentUserCapabilities(accessor.Object), NullLogger<GlBoundaryAuthorizer>.Instance);
    }

    private static PostingRequest BalancedRequest() => new()
    {
        BookId = BookId,
        EntryDate = new DateOnly(2026, 1, 15),
        Source = JournalSource.Manual,
        CurrencyId = UsdId,
        Memo = "test",
        Lines =
        [
            new PostingLine { GlAccountId = CashId, Debit = 100m },
            new PostingLine { GlAccountId = RevenueId, Credit = 100m },
        ],
    };

    // ── §5.5 opening-balances hard-gate ──────────────────────────────────────

    [Fact]
    public async Task HardGate_RefusesFullGl_WhenNoOpeningBalancesLoaded()
    {
        using var db = await SeedAsync();
        var gate = new GlCapabilityGate(db);

        (await gate.AreOpeningBalancesLoadedAsync(BookId)).Should().BeFalse();

        var eligibility = await gate.EvaluateAsync(BookId);
        eligibility.CanEnable.Should().BeFalse();
        eligibility.Reason.Should().Contain("opening balances");
    }

    [Fact]
    public async Task HardGate_AllowsFullGl_OnceConversionOpeningJournalPosted()
    {
        using var db = await SeedAsync();

        // §7A: opening balances enter as a posted Source=Conversion journal.
        db.JournalEntries.Add(new JournalEntry
        {
            BookId = BookId, EntryNumber = 1, EntryDate = new DateOnly(2026, 1, 1),
            FiscalPeriodId = OpenPeriodId, FiscalYearId = FiscalYearId,
            Source = JournalSource.Conversion, CurrencyId = UsdId,
            Status = JournalEntryStatus.Posted, PostedBy = 1, PostedAt = DateTimeOffset.UtcNow,
        });
        await db.SaveChangesAsync();

        var gate = new GlCapabilityGate(db);
        (await gate.AreOpeningBalancesLoadedAsync(BookId)).Should().BeTrue();

        var eligibility = await gate.EvaluateAsync(BookId);
        eligibility.CanEnable.Should().BeTrue();
        eligibility.Reason.Should().BeNull();
    }

    [Fact]
    public async Task HardGate_RefusesUnknownBook()
    {
        using var db = await SeedAsync();
        var gate = new GlCapabilityGate(db);

        var eligibility = await gate.EvaluateAsync(bookId: 999);
        eligibility.CanEnable.Should().BeFalse();
        eligibility.Reason.Should().Contain("does not exist");
    }

    // ── §5.7 SoD enforced at the engine boundary (authorizer injected) ───────

    [Fact]
    public async Task Engine_WithControllerAuthorizer_PostsSuccessfully()
    {
        using var db = await SeedAsync();
        var engine = new ForgeGlPostingEngine(
            db, new AccountDeterminationResolver(db), new FakeAllocator(), new SystemClock(),
            AuthorizerForRoles(7, "Controller"));

        var entry = await engine.PostAsync(BalancedRequest(), postedByUserId: 7);
        entry.Status.Should().Be(JournalEntryStatus.Posted);
    }

    [Fact]
    public async Task Engine_WithNonControllerAuthorizer_DeniesPost()
    {
        using var db = await SeedAsync();
        var engine = new ForgeGlPostingEngine(
            db, new AccountDeterminationResolver(db), new FakeAllocator(), new SystemClock(),
            AuthorizerForRoles(7, "Admin")); // bare Admin → off the books

        var act = async () => await engine.PostAsync(BalancedRequest(), postedByUserId: 7);

        (await act.Should().ThrowAsync<GlAuthorizationException>())
            .Which.RequiredCapability.Should().Be(GlCapability.PostJournalEntry);
        // Fail-safe: nothing persisted.
        (await db.JournalEntries.IgnoreQueryFilters().CountAsync()).Should().Be(0);
    }

    [Fact]
    public async Task Engine_WithoutAuthorizer_StaysDarkPhase0Seam_Posts()
    {
        // The null-authorizer ctor (engine's own unit-test seam) treats the
        // boundary as not-yet-wired and proceeds — matching the existing
        // PostingEngine/handler tests that construct the engine without identity.
        using var db = await SeedAsync();
        var engine = new ForgeGlPostingEngine(
            db, new AccountDeterminationResolver(db), new FakeAllocator(), new SystemClock());

        var entry = await engine.PostAsync(BalancedRequest(), postedByUserId: 7);
        entry.Status.Should().Be(JournalEntryStatus.Posted);
    }

    private static PostingRequest SubledgerRequest(string key) => new()
    {
        BookId = BookId,
        EntryDate = new DateOnly(2026, 1, 15),
        Source = JournalSource.AR,
        SourceType = "Invoice",
        SourceId = 1,
        IdempotencyKey = key,
        CurrencyId = UsdId,
        Memo = "test",
        Lines =
        [
            new PostingLine { GlAccountId = CashId, Debit = 100m },
            new PostingLine { GlAccountId = RevenueId, Credit = 100m },
        ],
    };

    [Theory]
    [InlineData("OfficeManager")]
    [InlineData("Procurement")]
    [InlineData("ProductionWorker")]
    [InlineData("Admin")]
    public async Task SubledgerPosting_ByBusinessRoleWithoutPostJournalEntry_PostsAsSystem(string role)
    {
        using var db = await SeedAsync();
        var engine = new ForgeGlPostingEngine(
            db, new AccountDeterminationResolver(db), new FakeAllocator(), new SystemClock(),
            AuthorizerForRoles(7, role));

        var entry = await engine.PostAsSystemAsync(SubledgerRequest("AR:Invoice:1:FINALIZE"), actingUserId: 7);

        entry.Status.Should().Be(JournalEntryStatus.Posted);
        entry.PostedBy.Should().Be(7);
        GlSystemPostingScope.IsActive.Should().BeFalse();
    }

    [Fact]
    public async Task SubledgerPosting_RefusesManualJournal()
    {
        using var db = await SeedAsync();
        var engine = new ForgeGlPostingEngine(
            db, new AccountDeterminationResolver(db), new FakeAllocator(), new SystemClock(),
            AuthorizerForRoles(7, "Controller"));

        var act = async () => await engine.PostAsSystemAsync(BalancedRequest(), actingUserId: 7);

        await act.Should().ThrowAsync<ArgumentException>();
        (await db.JournalEntries.IgnoreQueryFilters().CountAsync()).Should().Be(0);
    }

    [Theory]
    [InlineData("OfficeManager")]
    [InlineData("Procurement")]
    [InlineData("Admin")]
    public async Task ManualJournal_AfterSubledgerPosting_StillRequiresPostJournalEntry(string role)
    {
        using var db = await SeedAsync();
        var engine = new ForgeGlPostingEngine(
            db, new AccountDeterminationResolver(db), new FakeAllocator(), new SystemClock(),
            AuthorizerForRoles(7, role));
        await engine.PostAsSystemAsync(SubledgerRequest("AR:Invoice:1:FINALIZE"), actingUserId: 7);

        var act = async () => await engine.PostAsync(BalancedRequest(), postedByUserId: 7);

        (await act.Should().ThrowAsync<GlAuthorizationException>())
            .Which.RequiredCapability.Should().Be(GlCapability.PostJournalEntry);
    }

    [Fact]
    public async Task SubledgerReversal_ByBusinessRole_ReversesAsSystem_WhileManualReversalIsDenied()
    {
        using var db = await SeedAsync();
        var engine = new ForgeGlPostingEngine(
            db, new AccountDeterminationResolver(db), new FakeAllocator(), new SystemClock(),
            AuthorizerForRoles(7, "OfficeManager"));
        var first = await engine.PostAsSystemAsync(SubledgerRequest("AR:Payment:1:CASH"), actingUserId: 7);
        var second = await engine.PostAsSystemAsync(SubledgerRequest("AR:Payment:2:CASH"), actingUserId: 7);

        var manual = async () => await engine.ReverseAsync(second.Id, new DateOnly(2026, 1, 20), "manual", 7);
        (await manual.Should().ThrowAsync<GlAuthorizationException>())
            .Which.RequiredCapability.Should().Be(GlCapability.ReverseJournalEntry);

        var reversal = await engine.ReverseAsSystemAsync(first.Id, new DateOnly(2026, 1, 20), "voided", actingUserId: 7);

        reversal.Status.Should().Be(JournalEntryStatus.Posted);
        reversal.ReversalOfEntryId.Should().Be(first.Id);
    }
}
