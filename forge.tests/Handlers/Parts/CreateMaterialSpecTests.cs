using FluentAssertions;
using FluentValidation;
using Microsoft.EntityFrameworkCore;

using Forge.Api.Features.Parts;
using Forge.Core.Models;
using Forge.Data.Context;
using Forge.Tests.Helpers;

using ReferenceDataEntity = Forge.Core.Entities.ReferenceData;

namespace Forge.Tests.Handlers.Parts;

public class CreateMaterialSpecTests
{
    private const string Group = CreateMaterialSpecHandler.GroupCode;

    private readonly AppDbContext _db = TestDbContextFactory.Create();
    private readonly CreateMaterialSpecHandler _handler;

    public CreateMaterialSpecTests()
    {
        _handler = new CreateMaterialSpecHandler(_db);
    }

    private async Task<ReferenceDataEntity> SeedAsync(string group, string code, string label, int sort, int? parentId = null)
    {
        var row = new ReferenceDataEntity
        {
            GroupCode = group, Code = code, Label = label, SortOrder = sort, IsActive = true, ParentId = parentId,
        };
        _db.ReferenceData.Add(row);
        await _db.SaveChangesAsync();
        return row;
    }

    private Task<ReferenceDataResponseModel> Send(string label, int? parentId = null, string? newCategory = null)
        => _handler.Handle(
            new CreateMaterialSpecCommand(new CreateMaterialSpecRequestModel(label, parentId, newCategory)),
            CancellationToken.None);

    [Fact]
    public async Task Creates_a_leaf_under_an_existing_parent()
    {
        var aluminum = await SeedAsync(Group, "aluminum", "Aluminum", 1);
        await SeedAsync(Group, "aluminum-6061-t6", "6061-T6", 4, aluminum.Id);

        var result = await Send("  7075-T6 ", aluminum.Id);

        result.Label.Should().Be("7075-T6");
        result.ParentId.Should().Be(aluminum.Id);
        result.Code.Should().Be("aluminum-7075-t6");
        result.SortOrder.Should().Be(14);
        result.IsActive.Should().BeTrue();
        result.IsSeedData.Should().BeFalse();

        var log = await _db.ActivityLogs.SingleAsync();
        log.EntityType.Should().Be("ReferenceData");
        log.EntityId.Should().Be(result.Id);
        log.Action.Should().Be("created");
        log.Description.Should().Contain("Aluminum / 7075-T6");
    }

    [Fact]
    public async Task Creates_a_new_category_then_the_leaf_under_it()
    {
        await SeedAsync(Group, "aluminum", "Aluminum", 20);

        var result = await Send("Grade 9", newCategory: "Magnesium");

        var category = await _db.ReferenceData.SingleAsync(r => r.GroupCode == Group && r.Label == "Magnesium");
        category.ParentId.Should().BeNull();
        category.Code.Should().Be("magnesium");
        category.SortOrder.Should().Be(30);

        result.ParentId.Should().Be(category.Id);
        result.Code.Should().Be("magnesium-grade-9");
        result.SortOrder.Should().Be(10);

        var logged = await _db.ActivityLogs.Select(a => a.EntityId).ToListAsync();
        logged.Should().BeEquivalentTo(new[] { category.Id, result.Id });
    }

    [Fact]
    public async Task Creates_a_standalone_top_level_material_with_neither_parent_nor_category()
    {
        var result = await Send("Wood");

        result.ParentId.Should().BeNull();
        result.Code.Should().Be("wood");
        result.SortOrder.Should().Be(10);
    }

    [Fact]
    public async Task Duplicate_label_under_the_same_parent_is_a_conflict()
    {
        var aluminum = await SeedAsync(Group, "aluminum", "Aluminum", 1);
        await SeedAsync(Group, "aluminum-6061-t6", "6061-T6", 1, aluminum.Id);

        var act = () => Send("6061-t6", aluminum.Id);

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("That material already exists: Aluminum / 6061-T6.");
        (await _db.ReferenceData.CountAsync()).Should().Be(2);
        (await _db.ActivityLogs.AnyAsync()).Should().BeFalse();
    }

    [Fact]
    public async Task Same_label_under_a_different_parent_gets_a_unique_code()
    {
        var aluminum = await SeedAsync(Group, "aluminum", "Aluminum", 1);
        await SeedAsync(Group, "aluminum-x", "Other", 1);

        var result = await Send("X", aluminum.Id);

        result.Code.Should().Be("aluminum-x-2");
    }

    [Fact]
    public async Task Parent_from_another_group_is_rejected()
    {
        var foreign = await SeedAsync("part.item_kind", "aluminum", "Aluminum", 1);

        var act = () => Send("7075-T6", foreign.Id);

        await act.Should().ThrowAsync<ValidationException>();
        (await _db.ReferenceData.CountAsync()).Should().Be(1);
    }

    [Fact]
    public async Task Parent_that_is_itself_a_leaf_is_rejected()
    {
        var aluminum = await SeedAsync(Group, "aluminum", "Aluminum", 1);
        var leaf = await SeedAsync(Group, "aluminum-6061-t6", "6061-T6", 1, aluminum.Id);

        var act = () => Send("Sub-grade", leaf.Id);

        await act.Should().ThrowAsync<ValidationException>();
    }

    [Theory]
    [InlineData("", null, null, false)]
    [InlineData("   ", null, null, false)]
    [InlineData("7075-T6", 5, "Magnesium", false)]
    [InlineData("7075-T6", null, " ", false)]
    [InlineData("7075-T6", 5, null, true)]
    [InlineData("7075-T6", null, "Magnesium", true)]
    public void Validator_enforces_label_and_parent_rules(string label, int? parentId, string? category, bool valid)
    {
        var result = new CreateMaterialSpecValidator().Validate(
            new CreateMaterialSpecCommand(new CreateMaterialSpecRequestModel(label, parentId, category)));

        result.IsValid.Should().Be(valid);
    }

    [Fact]
    public void Validator_rejects_a_label_over_200_characters()
    {
        var result = new CreateMaterialSpecValidator().Validate(
            new CreateMaterialSpecCommand(new CreateMaterialSpecRequestModel(new string('a', 201))));

        result.IsValid.Should().BeFalse();
    }
}
