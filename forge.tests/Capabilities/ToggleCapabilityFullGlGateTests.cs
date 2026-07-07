using FluentAssertions;
using Microsoft.AspNetCore.SignalR;
using Moq;

using Forge.Api.Capabilities;
using Forge.Api.Features.Accounting;
using Forge.Api.Features.Capabilities.Toggle;
using Forge.Api.Hubs;
using Forge.Api.Services;
using Forge.Core.Entities;
using Forge.Core.Entities.Accounting;
using Forge.Core.Enums.Accounting;
using Forge.Data.Context;
using Forge.Tests.Helpers;

namespace Forge.Tests.Capabilities;

/// <summary>
/// §5.5 / §7A opening-balances hard-gate, WIRED (2026-07-07): enabling CAP-ACCT-FULLGL through
/// <see cref="ToggleCapabilityHandler"/> is refused (409) while any active book lacks a posted
/// <see cref="JournalSource.Conversion"/> opening journal, and succeeds once one exists. Other
/// capabilities are untouched by the gate.
/// </summary>
public class ToggleCapabilityFullGlGateTests
{
    private const int BookId = 1;

    private sealed class SnapshotStub : ICapabilitySnapshotProvider
    {
        public CapabilitySnapshot Current { get; } = new(
            new Dictionary<string, bool>(StringComparer.Ordinal)
            {
                ["CAP-ACCT-BUILTIN"] = true,   // FULLGL's dependency
                ["CAP-ACCT-EXTERNAL"] = false, // FULLGL's mutex peer
                ["CAP-ACCT-FULLGL"] = false,
            },
            DateTimeOffset.UtcNow);

        public bool IsEnabled(string code) => Current.IsEnabled(code);

        public Task RefreshAsync(CancellationToken ct = default) => Task.CompletedTask;
    }

    private static ToggleCapabilityHandler Handler(AppDbContext db)
    {
        var audit = new Mock<ISystemAuditWriter>();
        audit
            .Setup(a => a.WriteAsync(It.IsAny<string>(), It.IsAny<int>(), It.IsAny<string?>(), It.IsAny<int?>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var proxy = new Mock<IClientProxy>();
        proxy
            .Setup(p => p.SendCoreAsync(It.IsAny<string>(), It.IsAny<object?[]>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        var clients = new Mock<IHubClients>();
        clients.SetupGet(c => c.All).Returns(proxy.Object);
        var hub = new Mock<IHubContext<NotificationHub>>();
        hub.SetupGet(h => h.Clients).Returns(clients.Object);

        return new ToggleCapabilityHandler(db, new SnapshotStub(), audit.Object, hub.Object, new GlCapabilityGate(db));
    }

    private static AppDbContext SeededDb(bool withOpeningJournal)
    {
        var db = TestDbContextFactory.Create();
        db.Capabilities.Add(new Capability
        {
            Code = "CAP-ACCT-FULLGL",
            Area = "ACCT",
            Name = "Built-in full general ledger",
            Description = "test",
            Enabled = false,
        });
        db.Books.Add(new Book
        {
            Id = BookId,
            Code = "MAIN",
            Name = "Default Book",
            FunctionalCurrencyId = 1,
            ReportingTimeZone = "America/Denver",
            IsActive = true,
        });
        if (withOpeningJournal)
        {
            db.JournalEntries.Add(new JournalEntry
            {
                Id = 1,
                BookId = BookId,
                EntryNumber = 1,
                EntryDate = new DateOnly(2026, 1, 1),
                FiscalPeriodId = 1,
                FiscalYearId = 1,
                Source = JournalSource.Conversion,
                CurrencyId = 1,
                Status = JournalEntryStatus.Posted,
                Memo = "Opening balances",
            });
        }
        db.SaveChanges();
        return db;
    }

    [Fact]
    public async Task Enabling_FULLGL_is_refused_until_opening_balances_are_loaded()
    {
        await using var db = SeededDb(withOpeningJournal: false);
        var act = () => Handler(db).Handle(new ToggleCapabilityCommand("CAP-ACCT-FULLGL", Enabled: true), default);

        var ex = await act.Should().ThrowAsync<CapabilityMutationException>();
        ex.Which.Message.Should().Contain("opening balances");
        db.Capabilities.Single(c => c.Code == "CAP-ACCT-FULLGL").Enabled.Should().BeFalse();
    }

    [Fact]
    public async Task Enabling_FULLGL_succeeds_once_the_conversion_journal_is_posted()
    {
        await using var db = SeededDb(withOpeningJournal: true);
        var result = await Handler(db).Handle(new ToggleCapabilityCommand("CAP-ACCT-FULLGL", Enabled: true), default);

        result.Enabled.Should().BeTrue();
        db.Capabilities.Single(c => c.Code == "CAP-ACCT-FULLGL").Enabled.Should().BeTrue();
    }

    [Fact]
    public async Task Disabling_FULLGL_is_not_gated_on_opening_balances()
    {
        await using var db = SeededDb(withOpeningJournal: false);
        db.Capabilities.Single(c => c.Code == "CAP-ACCT-FULLGL").Enabled = true;
        db.SaveChanges();

        var result = await Handler(db).Handle(new ToggleCapabilityCommand("CAP-ACCT-FULLGL", Enabled: false), default);
        result.Enabled.Should().BeFalse();
    }
}
