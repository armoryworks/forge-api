using FluentAssertions;

using Forge.Api.Features.Eco;
using Forge.Core.Entities;
using Forge.Tests.Helpers;

namespace Forge.Tests.Handlers.Eco;

public class GetEcoAffectedItemLabelTests
{
    [Fact]
    public async Task Detail_names_affected_parts_by_part_number()
    {
        using var db = TestDbContextFactory.Create();
        var part = new Part { PartNumber = "PRT-7", Name = "Bracket" };
        db.Parts.Add(part);
        await db.SaveChangesAsync();
        var eco = new EngineeringChangeOrder { EcoNumber = "ECO-1", Title = "Thicker wall", Description = "x" };
        eco.AffectedItems.Add(new EcoAffectedItem { EntityType = "Part", EntityId = part.Id, ChangeDescription = "Rev B" });
        eco.AffectedItems.Add(new EcoAffectedItem { EntityType = "Drawing", EntityId = 99, ChangeDescription = "Redline" });
        db.EngineeringChangeOrders.Add(eco);
        await db.SaveChangesAsync();

        var result = await new GetEcoByIdHandler(db).Handle(new GetEcoByIdQuery(eco.Id), CancellationToken.None);

        result.AffectedItems.Single(i => i.EntityType == "Part").EntityLabel.Should().Be("PRT-7 — Bracket");
        result.AffectedItems.Single(i => i.EntityType == "Drawing").EntityLabel.Should().BeNull();
    }
}
