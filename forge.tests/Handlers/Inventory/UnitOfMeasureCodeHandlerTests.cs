using FluentAssertions;
using Microsoft.EntityFrameworkCore;

using Forge.Api.Features.Inventory;
using Forge.Core.Entities;
using Forge.Core.Enums;
using Forge.Core.Models;
using Forge.Tests.Helpers;

namespace Forge.Tests.Handlers.Inventory;

public class UnitOfMeasureCodeHandlerTests
{
    private static CreateUomRequestModel Request(string code, string name = "each") =>
        new(code, name, null, "Count", 0, false, 0);

    private static async Task<UnitOfMeasure> SeedAsync(Forge.Data.Context.AppDbContext db, string code)
    {
        var uom = new UnitOfMeasure { Code = code, Name = code, Category = UomCategory.Count, IsActive = true };
        db.UnitsOfMeasure.Add(uom);
        await db.SaveChangesAsync();
        return uom;
    }

    [Fact]
    public async Task Update_NameOnly_KeepsLowercaseCode()
    {
        using var db = TestDbContextFactory.Create();
        var ea = await SeedAsync(db, "ea");

        var result = await new UpdateUnitOfMeasureHandler(db)
            .Handle(new UpdateUnitOfMeasureCommand(ea.Id, Request("ea", "Each (renamed)")), default);

        result.Code.Should().Be("ea");
        (await db.UnitsOfMeasure.AsNoTracking().SingleAsync(u => u.Id == ea.Id)).Code.Should().Be("ea");
    }

    [Fact]
    public async Task Create_StoresTrimmedCodeAsEntered()
    {
        using var db = TestDbContextFactory.Create();

        var result = await new CreateUnitOfMeasureHandler(db)
            .Handle(new CreateUnitOfMeasureCommand(Request("  mm ", "millimetre")), default);

        result.Code.Should().Be("mm");
    }

    [Fact]
    public async Task Create_CaseOrWhitespaceVariantOfExistingCode_IsRejected()
    {
        using var db = TestDbContextFactory.Create();
        await SeedAsync(db, "ea");

        var act = () => new CreateUnitOfMeasureHandler(db)
            .Handle(new CreateUnitOfMeasureCommand(Request(" EA ")), default);

        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("*already exists*");
        (await db.UnitsOfMeasure.CountAsync()).Should().Be(1);
    }

    [Fact]
    public async Task Update_RenameToCaseVariantOfAnotherCode_IsRejected()
    {
        using var db = TestDbContextFactory.Create();
        await SeedAsync(db, "ea");
        var box = await SeedAsync(db, "box");

        var act = () => new UpdateUnitOfMeasureHandler(db)
            .Handle(new UpdateUnitOfMeasureCommand(box.Id, Request("EA")), default);

        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("*already exists*");
    }

    [Fact]
    public async Task Update_NameOnlyOnCodeWithCaseTwin_Succeeds()
    {
        using var db = TestDbContextFactory.Create();
        await SeedAsync(db, "ea");
        var upper = await SeedAsync(db, "EA");

        var result = await new UpdateUnitOfMeasureHandler(db)
            .Handle(new UpdateUnitOfMeasureCommand(upper.Id, Request("EA", "Each (upper)")), default);

        result.Code.Should().Be("EA");
        result.Name.Should().Be("Each (upper)");
    }

    [Fact]
    public async Task Update_CaseOnlyChangeOfOwnCode_Succeeds()
    {
        using var db = TestDbContextFactory.Create();
        var ea = await SeedAsync(db, "ea");

        var result = await new UpdateUnitOfMeasureHandler(db)
            .Handle(new UpdateUnitOfMeasureCommand(ea.Id, Request("Ea")), default);

        result.Code.Should().Be("Ea");
    }
}
