using FluentAssertions;

using Forge.Api.Features.Parts;
using Forge.Core.Entities;
using Forge.Core.Enums;
using Forge.Data.Context;
using Forge.Tests.Helpers;

namespace Forge.Tests.Handlers.Parts;

public class GetPartWhereUsedTests
{
    private readonly AppDbContext _db = TestDbContextFactory.Create();
    private readonly GetPartWhereUsedHandler _handler;

    public GetPartWhereUsedTests()
    {
        _handler = new GetPartWhereUsedHandler(_db);
    }

    private async Task<Part> SeedPartAsync(string partNumber, ProcurementSource source, InventoryClass inventoryClass)
    {
        var part = new Part
        {
            PartNumber = partNumber,
            Name = $"{partNumber} name",
            Revision = "B",
            ProcurementSource = source,
            InventoryClass = inventoryClass,
            Status = PartStatus.Active,
        };
        _db.Parts.Add(part);
        await _db.SaveChangesAsync();
        return part;
    }

    private async Task AddBomLineAsync(Part parent, Part child, decimal quantity, BOMSourceType sourceType)
    {
        _db.BOMLines.Add(new BOMLine
        {
            ParentPartId = parent.Id,
            ChildPartId = child.Id,
            Quantity = quantity,
            SourceType = sourceType,
        });
        await _db.SaveChangesAsync();
    }

    private async Task AddJobAsync(Part part, string jobNumber, bool archived = false, bool completed = false)
    {
        _db.Jobs.Add(new Job
        {
            JobNumber = jobNumber,
            Title = jobNumber,
            PartId = part.Id,
            IsArchived = archived,
            CompletedDate = completed ? new DateTimeOffset(2026, 1, 15, 0, 0, 0, TimeSpan.Zero) : null,
        });
        await _db.SaveChangesAsync();
    }

    [Fact]
    public async Task Handle_DirectParent_ReturnsParentWithOpenWorkOrderCount()
    {
        var bracket = await SeedPartAsync("PRT-00010", ProcurementSource.Make, InventoryClass.Component);
        var assembly = await SeedPartAsync("ASM-00020", ProcurementSource.Make, InventoryClass.Subassembly);
        await AddBomLineAsync(assembly, bracket, 4m, BOMSourceType.Make);
        await AddJobAsync(assembly, "J-1");
        await AddJobAsync(assembly, "J-2");
        await AddJobAsync(assembly, "J-3", completed: true);
        await AddJobAsync(assembly, "J-4", archived: true);

        var result = await _handler.Handle(new GetPartWhereUsedQuery(bracket.Id), CancellationToken.None);

        result.Should().ContainSingle();
        var row = result[0];
        row.ParentPartId.Should().Be(assembly.Id);
        row.ParentPartNumber.Should().Be("ASM-00020");
        row.ParentName.Should().Be("ASM-00020 name");
        row.ParentRevision.Should().Be("B");
        row.QuantityPer.Should().Be(4m);
        row.SourceType.Should().Be(BOMSourceType.Make);
        row.OpenWorkOrderCount.Should().Be(2);
    }

    [Fact]
    public async Task Handle_NoParents_ReturnsEmpty()
    {
        var loose = await SeedPartAsync("PRT-00030", ProcurementSource.Buy, InventoryClass.Raw);

        var result = await _handler.Handle(new GetPartWhereUsedQuery(loose.Id), CancellationToken.None);

        result.Should().BeEmpty();
    }

    [Fact]
    public async Task Handle_DeletedParent_IsExcluded()
    {
        var bar = await SeedPartAsync("PRT-00040", ProcurementSource.Buy, InventoryClass.Raw);
        var live = await SeedPartAsync("ASM-00041", ProcurementSource.Make, InventoryClass.Subassembly);
        var deleted = await SeedPartAsync("ASM-00042", ProcurementSource.Make, InventoryClass.Subassembly);
        await AddBomLineAsync(live, bar, 1m, BOMSourceType.Buy);
        await AddBomLineAsync(deleted, bar, 2m, BOMSourceType.Buy);
        deleted.DeletedAt = new DateTimeOffset(2026, 2, 1, 0, 0, 0, TimeSpan.Zero);
        await _db.SaveChangesAsync();

        var result = await _handler.Handle(new GetPartWhereUsedQuery(bar.Id), CancellationToken.None);

        result.Select(r => r.ParentPartNumber).Should().Equal("ASM-00041");
    }

    [Fact]
    public async Task Handle_UnknownPart_ThrowsKeyNotFound()
    {
        var act = () => _handler.Handle(new GetPartWhereUsedQuery(999_999), CancellationToken.None);

        await act.Should().ThrowAsync<KeyNotFoundException>();
    }
}
