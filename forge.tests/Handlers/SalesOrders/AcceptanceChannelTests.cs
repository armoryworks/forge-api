using FluentAssertions;
using MediatR;
using Moq;

using Forge.Api.Features.SalesOrders.Acceptance;
using Forge.Core.Entities;
using Forge.Core.Enums;
using Forge.Data.Context;
using Forge.Integrations;
using Forge.Tests.Helpers;

namespace Forge.Tests.Handlers.SalesOrders;

/// <summary>Acceptance capture channels produce an Accepted record that opens the gate (portal crypto too).</summary>
public class AcceptanceChannelTests
{
    private static async Task<int> SeedOrderAsync(AppDbContext db)
    {
        var so = new SalesOrder { OrderNumber = $"SO-{Guid.NewGuid():N}", CustomerId = 1, Status = SalesOrderStatus.Draft };
        db.Add(so);
        await db.SaveChangesAsync();
        return so.Id;
    }

    [Fact]
    public async Task Verbal_manual_acceptance_creates_accepted_record()
    {
        using var db = TestDbContextFactory.Create();
        var soId = await SeedOrderAsync(db);
        var handler = new RecordManualAcceptanceHandler(db, Mock.Of<IMediator>(), new SystemClock());

        var result = await handler.Handle(
            new RecordManualAcceptanceCommand(soId, AcceptanceMethod.Verbal, "Accepted by phone with J. Doe", null),
            CancellationToken.None);

        result.Status.Should().Be("Accepted");
        result.Method.Should().Be("Verbal");
        var gate = new SalesOrderAcceptanceGate(db, new StubCapabilitySnapshotProvider("CAP-O2C-SO-ACCEPTANCE"));
        (await gate.IsAcceptedAsync(soId)).Should().BeTrue("a recorded acceptance opens the gate");
    }

    [Fact]
    public async Task External_system_acceptance_creates_accepted_record()
    {
        using var db = TestDbContextFactory.Create();
        var soId = await SeedOrderAsync(db);
        var handler = new RecordExternalAcceptanceHandler(db, new SystemClock());

        var result = await handler.Handle(
            new RecordExternalAcceptanceCommand(soId, "ACME-ACCEPT-9931", "ACME Procurement", null),
            CancellationToken.None);

        result.Status.Should().Be("Accepted");
        result.Method.Should().Be("ExternalSystem");
        result.ProviderReference.Should().Be("ACME-ACCEPT-9931");
    }

    [Fact]
    public void Portal_key_verification_round_trips_and_rejects_wrong_key()
    {
        var hash = AcceptancePortalCrypto.HashKey("PO-4471");
        AcceptancePortalCrypto.KeyMatches(" po-4471 ", hash).Should().BeTrue("case/whitespace-insensitive match");
        AcceptancePortalCrypto.KeyMatches("PO-9999", hash).Should().BeFalse();
        AcceptancePortalCrypto.GenerateToken().Should().NotBeNullOrWhiteSpace();
    }
}
