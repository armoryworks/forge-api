using FluentAssertions;
using Microsoft.EntityFrameworkCore;

using Forge.Api.Features.Parts;
using Forge.Core.Entities;
using Forge.Core.Models;
using Forge.Data.Context;
using Forge.Tests.Helpers;

using ReferenceDataEntity = Forge.Core.Entities.ReferenceData;

namespace Forge.Tests.Handlers.Parts;

public class MaterialSpecManagementTests
{
    private const string Group = CreateMaterialSpecHandler.GroupCode;

    private readonly AppDbContext _db = TestDbContextFactory.Create();

    private async Task<ReferenceDataEntity> SeedAsync(string code, string label, int sort, int? parentId = null, bool active = true)
    {
        var row = new ReferenceDataEntity
        {
            GroupCode = Group, Code = code, Label = label, SortOrder = sort, IsActive = active, ParentId = parentId,
        };
        _db.ReferenceData.Add(row);
        await _db.SaveChangesAsync();
        return row;
    }

    [Fact]
    public async Task Lists_categories_with_their_specs_and_part_counts_and_hides_retired_unless_asked()
    {
        var steel = await SeedAsync("steel", "Steel", 20);
        var aluminum = await SeedAsync("aluminum", "Aluminum", 10);
        var a6061 = await SeedAsync("aluminum-6061", "6061-T6", 10, aluminum.Id);
        await SeedAsync("aluminum-2024", "2024-T3", 20, aluminum.Id, active: false);
        await SeedAsync("steel-1018", "1018", 10, steel.Id);
        _db.Parts.AddRange(
            new Part { PartNumber = "P-1", Name = "Bracket", MaterialSpecId = a6061.Id },
            new Part { PartNumber = "P-2", Name = "Plate", MaterialSpecId = a6061.Id });
        await _db.SaveChangesAsync();

        var active = await new GetMaterialSpecsHandler(_db).Handle(new GetMaterialSpecsQuery(false), CancellationToken.None);
        var all = await new GetMaterialSpecsHandler(_db).Handle(new GetMaterialSpecsQuery(true), CancellationToken.None);

        active.Select(r => r.Label).Should().Equal("Aluminum", "6061-T6", "Steel", "1018");
        active.Single(r => r.Id == a6061.Id).Should().Match<MaterialSpecResponseModel>(r => r.PartCount == 2 && r.CategoryLabel == "Aluminum");
        all.Select(r => r.Label).Should().Equal("Aluminum", "6061-T6", "2024-T3", "Steel", "1018");
    }

    [Fact]
    public async Task Rename_keeps_the_code_logs_it_and_refuses_a_sibling_name()
    {
        var aluminum = await SeedAsync("aluminum", "Aluminum", 10);
        var a6061 = await SeedAsync("aluminum-6061", "6061-T6", 10, aluminum.Id);
        await SeedAsync("aluminum-7075", "7075-T6", 20, aluminum.Id);
        var handler = new UpdateMaterialSpecHandler(_db);

        var renamed = await handler.Handle(new UpdateMaterialSpecCommand(a6061.Id, new UpdateMaterialSpecRequestModel(" 6061-T651 ")), CancellationToken.None);

        renamed.Label.Should().Be("6061-T651");
        renamed.Code.Should().Be("aluminum-6061");
        (await _db.ActivityLogs.SingleAsync()).Should().Match<ActivityLog>(l => l.EntityType == "ReferenceData" && l.Action == "updated");

        var duplicate = () => handler.Handle(new UpdateMaterialSpecCommand(a6061.Id, new UpdateMaterialSpecRequestModel("7075-t6")), CancellationToken.None);
        await duplicate.Should().ThrowAsync<InvalidOperationException>();
    }

    [Fact]
    public async Task Retire_and_reactivate_follow_the_category_rules()
    {
        var aluminum = await SeedAsync("aluminum", "Aluminum", 10);
        var a6061 = await SeedAsync("aluminum-6061", "6061-T6", 10, aluminum.Id);
        var handler = new SetMaterialSpecActiveHandler(_db);

        var retireCategory = () => handler.Handle(new SetMaterialSpecActiveCommand(aluminum.Id, false), CancellationToken.None);
        await retireCategory.Should().ThrowAsync<InvalidOperationException>().WithMessage("*Aluminum*");

        (await handler.Handle(new SetMaterialSpecActiveCommand(a6061.Id, false), CancellationToken.None)).IsActive.Should().BeFalse();
        (await handler.Handle(new SetMaterialSpecActiveCommand(aluminum.Id, false), CancellationToken.None)).IsActive.Should().BeFalse();

        var reactivateSpec = () => handler.Handle(new SetMaterialSpecActiveCommand(a6061.Id, true), CancellationToken.None);
        await reactivateSpec.Should().ThrowAsync<InvalidOperationException>();

        (await handler.Handle(new SetMaterialSpecActiveCommand(aluminum.Id, true), CancellationToken.None)).IsActive.Should().BeTrue();
        (await handler.Handle(new SetMaterialSpecActiveCommand(a6061.Id, true), CancellationToken.None)).IsActive.Should().BeTrue();
        (await _db.ActivityLogs.CountAsync(l => l.Action == "retired")).Should().Be(2);
    }

    [Fact]
    public async Task Another_reference_group_is_not_found()
    {
        var other = new ReferenceDataEntity { GroupCode = "part.item_kind", Code = "fastener", Label = "Fastener", SortOrder = 1, IsActive = true };
        _db.ReferenceData.Add(other);
        await _db.SaveChangesAsync();

        var act = () => new SetMaterialSpecActiveHandler(_db).Handle(new SetMaterialSpecActiveCommand(other.Id, false), CancellationToken.None);

        await act.Should().ThrowAsync<KeyNotFoundException>();
    }
}
