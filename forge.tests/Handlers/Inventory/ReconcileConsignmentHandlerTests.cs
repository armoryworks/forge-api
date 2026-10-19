using FluentAssertions;

using Forge.Api.Features.Inventory;
using Forge.Core.Entities;
using Forge.Core.Enums;
using Forge.Core.Models;
using Forge.Integrations;
using Forge.Tests.Helpers;

namespace Forge.Tests.Handlers.Inventory;

public class ReconcileConsignmentHandlerTests
{
    [Fact]
    public async Task Reconcile_TwiceAtTheSameCount_PostsTheVarianceOnce()
    {
        using var db = TestDbContextFactory.Create();
        var part = new Part { PartNumber = "P-300", Name = "Fastener" };
        db.Parts.Add(part);
        await db.SaveChangesAsync();
        var agreement = new ConsignmentAgreement
        {
            Direction = ConsignmentDirection.Inbound, VendorId = 1, PartId = part.Id, AgreedUnitPrice = 0.5m,
            EffectiveFrom = new DateOnly(2026, 1, 1),
        };
        db.ConsignmentAgreements.Add(agreement);
        await db.SaveChangesAsync();
        db.ConsignmentTransactions.AddRange(
            new ConsignmentTransaction { AgreementId = agreement.Id, Type = ConsignmentTransactionType.Receipt, Quantity = 2000m },
            new ConsignmentTransaction { AgreementId = agreement.Id, Type = ConsignmentTransactionType.Consumption, Quantity = 350m });
        await db.SaveChangesAsync();
        var handler = new ReconcileConsignmentHandler(db, new SystemClock());
        var command = new ReconcileConsignmentCommand(agreement.Id, new ReconcileConsignmentRequestModel { PhysicalCount = 1600m });

        var first = await handler.Handle(command, CancellationToken.None);
        var second = await handler.Handle(command, CancellationToken.None);

        first.Variance.Should().Be(-50m);
        second.BookQuantity.Should().Be(1600m);
        second.Variance.Should().Be(0m);
        second.AdjustmentTransactionId.Should().BeNull();
        db.ConsignmentTransactions.Count(t => t.Type == ConsignmentTransactionType.Reconciliation).Should().Be(1);
    }
}
