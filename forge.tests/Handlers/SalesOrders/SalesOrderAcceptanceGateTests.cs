using FluentAssertions;

using Forge.Api.Features.SalesOrders.Acceptance;
using Forge.Core.Entities;
using Forge.Core.Enums;
using Forge.Data.Context;
using Forge.Tests.Helpers;

namespace Forge.Tests.Handlers.SalesOrders;

/// <summary>
/// The customer-acceptance production gate: transparent when the capability is off, a hard block when
/// on and no Accepted record exists. Channel-agnostic — only an Accepted status satisfies it.
/// </summary>
public class SalesOrderAcceptanceGateTests
{
    private const string Cap = "CAP-O2C-SO-ACCEPTANCE";

    private static async Task<int> SeedOrderAsync(AppDbContext db)
    {
        var so = new SalesOrder { OrderNumber = $"SO-{Guid.NewGuid():N}", CustomerId = 1, Status = SalesOrderStatus.Draft };
        db.Add(so);
        await db.SaveChangesAsync();
        return so.Id;
    }

    private static async Task AddAcceptanceAsync(AppDbContext db, int soId, AcceptanceStatus status)
    {
        db.Add(new SalesOrderAcceptance { SalesOrderId = soId, Status = status, Method = AcceptanceMethod.ManualUpload });
        await db.SaveChangesAsync();
    }

    [Fact]
    public async Task Gate_off_never_blocks()
    {
        using var db = TestDbContextFactory.Create();
        var soId = await SeedOrderAsync(db);
        var gate = new SalesOrderAcceptanceGate(db, StubCapabilitySnapshotProvider.Off);

        gate.IsEnabled.Should().BeFalse();
        var act = () => gate.EnsureReleasableAsync(soId);
        await act.Should().NotThrowAsync("gate is transparent when the capability is disabled");
    }

    [Fact]
    public async Task Gate_on_blocks_when_no_acceptance()
    {
        using var db = TestDbContextFactory.Create();
        var soId = await SeedOrderAsync(db);
        var gate = new SalesOrderAcceptanceGate(db, new StubCapabilitySnapshotProvider(Cap));

        var act = () => gate.EnsureReleasableAsync(soId);
        await act.Should().ThrowAsync<InvalidOperationException>();
    }

    [Fact]
    public async Task Gate_on_allows_when_accepted()
    {
        using var db = TestDbContextFactory.Create();
        var soId = await SeedOrderAsync(db);
        await AddAcceptanceAsync(db, soId, AcceptanceStatus.Accepted);
        var gate = new SalesOrderAcceptanceGate(db, new StubCapabilitySnapshotProvider(Cap));

        (await gate.IsAcceptedAsync(soId)).Should().BeTrue();
        var act = () => gate.EnsureReleasableAsync(soId);
        await act.Should().NotThrowAsync();
    }

    [Theory]
    [InlineData(AcceptanceStatus.Pending)]
    [InlineData(AcceptanceStatus.Declined)]
    [InlineData(AcceptanceStatus.Revoked)]
    [InlineData(AcceptanceStatus.Expired)]
    public async Task Gate_on_ignores_non_accepted_states(AcceptanceStatus status)
    {
        using var db = TestDbContextFactory.Create();
        var soId = await SeedOrderAsync(db);
        await AddAcceptanceAsync(db, soId, status);
        var gate = new SalesOrderAcceptanceGate(db, new StubCapabilitySnapshotProvider(Cap));

        var act = () => gate.EnsureReleasableAsync(soId);
        await act.Should().ThrowAsync<InvalidOperationException>("only an Accepted record opens the gate");
    }
}
