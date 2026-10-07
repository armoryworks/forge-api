using FluentAssertions;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Moq;

using Forge.Api.Features.Parts;
using Forge.Api.Services;
using Forge.Core.Entities;
using Forge.Core.Enums;
using Forge.Core.Interfaces;
using Forge.Core.Models;
using Forge.Data.Repositories;
using Forge.Tests.Helpers;

namespace Forge.Tests.Handlers.Parts;

/// <summary>
/// The clone runs in one explicit transaction that also spans the nested saves of the barcode,
/// identifier, BOM revision and standard-cost services. The InMemory provider ignores transactions,
/// so only a real Postgres proves that a failure late in the clone leaves no partial part behind.
/// </summary>
[Collection(PostgresCollection.Name)]
public sealed class ClonePartPostgresTests(PostgresFixture fixture)
{
    [Fact]
    public async Task Clone_rolls_back_every_row_when_a_late_step_fails()
    {
        int sourceId, childId;
        var runId = Guid.NewGuid().ToString("N");
        var cloneName = $"Clone rollback {runId}";

        await using (var seed = fixture.CreateContext())
        {
            var source = new Part
            {
                PartNumber = $"CLN-S-{Guid.NewGuid():N}"[..16],
                Name = "Clone rollback source",
                ProcurementSource = ProcurementSource.Make,
                InventoryClass = InventoryClass.Subassembly,
            };
            var child = new Part
            {
                PartNumber = $"CLN-C-{Guid.NewGuid():N}"[..16],
                Name = "Clone rollback child",
                ProcurementSource = ProcurementSource.Buy,
                InventoryClass = InventoryClass.Component,
            };
            seed.Parts.AddRange(source, child);
            await seed.SaveChangesAsync();

            var bomLine = new BOMLine { ParentPartId = source.Id, ChildPartId = child.Id, Quantity = 2, SortOrder = 1 };
            seed.BOMLines.Add(bomLine);
            var first = new Operation { PartId = source.Id, StepNumber = 10, Title = $"Cut {runId}" };
            var second = new Operation { PartId = source.Id, StepNumber = 20, Title = $"Weld {runId}" };
            seed.Operations.AddRange(first, second);
            await seed.SaveChangesAsync();

            seed.OperationMaterials.Add(new OperationMaterial { OperationId = first.Id, BomLineId = bomLine.Id, Quantity = 2 });
            await seed.SaveChangesAsync();
            sourceId = source.Id;
            childId = child.Id;
        }

        await using (var db = fixture.CreateContext())
        {
            var identifiers = new Mock<IBusinessIdentifierService>();
            identifiers.Setup(i => i.IssueAsync(It.IsAny<BusinessEntityType>(), It.IsAny<int>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(new BusinessIdentifier());
            var sender = new Mock<ISender>();
            sender.Setup(s => s.Send(It.IsAny<RecalculatePartStandardCostCommand>(), It.IsAny<CancellationToken>()))
                .ThrowsAsync(new InvalidOperationException("cost roll-up failed"));

            var handler = new ClonePartHandler(
                db,
                new PartRepository(db, Mock.Of<IPartPricingResolver>()),
                Mock.Of<ISystemSettingRepository>(),
                Mock.Of<IBarcodeService>(),
                identifiers.Object,
                Mock.Of<IBomRevisionService>(),
                Mock.Of<ISyncQueueRepository>(),
                Mock.Of<IAccountingProviderFactory>(),
                sender.Object,
                Mock.Of<ILogger<ClonePartHandler>>());

            var act = () => handler.Handle(
                new ClonePartCommand(sourceId, new ClonePartRequestModel(cloneName)), CancellationToken.None);

            await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("cost roll-up failed");
        }

        await using var verify = fixture.CreateContext();
        (await verify.Parts.IgnoreQueryFilters().AnyAsync(p => p.Name == cloneName)).Should().BeFalse();
        (await verify.Operations.CountAsync(o => o.Title.EndsWith(runId)))
            .Should().Be(2, "only the source routing may remain");
        (await verify.OperationMaterials.CountAsync(m => m.Operation.Title.EndsWith(runId))).Should().Be(1);
        (await verify.BOMLines.CountAsync(b => b.ChildPartId == childId)).Should().Be(1);
    }
}
