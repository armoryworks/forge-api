using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Moq;

using Forge.Api.Features.AutoPo;
using Forge.Core.Entities;
using Forge.Core.Enums;
using Forge.Core.Interfaces;
using Forge.Data.Repositories;
using Forge.Tests.Helpers;

namespace Forge.Tests.Handlers.AutoPo;

[Collection(PostgresCollection.Name)]
public sealed class ConvertAutoPoSuggestionPostgresTests(PostgresFixture fixture)
{
    private static string Unique(string prefix) => $"{prefix}-{Guid.NewGuid():N}"[..16];

    [Fact]
    public async Task Convert_InsertsThePoAndLinksTheSuggestionToItsRealKey()
    {
        int suggestionId;
        await using (var seed = fixture.CreateContext())
        {
            var vendor = new Vendor { CompanyName = Unique("APO-VEND") };
            seed.Vendors.Add(vendor);
            await seed.SaveChangesAsync();

            var part = new Part { PartNumber = Unique("APO-PART"), Description = "Bought bushing", PreferredVendorId = vendor.Id };
            seed.Parts.Add(part);
            await seed.SaveChangesAsync();

            var suggestion = new AutoPoSuggestion
            {
                PartId = part.Id,
                VendorId = vendor.Id,
                SuggestedQty = 12m,
                NeededByDate = DateTimeOffset.UtcNow.AddDays(10),
                Status = AutoPoSuggestionStatus.Pending,
            };
            seed.AutoPoSuggestions.Add(suggestion);
            await seed.SaveChangesAsync();
            suggestionId = suggestion.Id;
        }

        int poId;
        await using (var db = fixture.CreateContext())
        {
            var handler = new ConvertAutoPoSuggestionHandler(
                db, new PurchaseOrderRepository(db), Mock.Of<IBarcodeService>());
            poId = await handler.Handle(new ConvertAutoPoSuggestionCommand(suggestionId, UserId: 0), CancellationToken.None);
        }

        await using var verify = fixture.CreateContext();
        poId.Should().BeGreaterThan(0);
        var stored = await verify.AutoPoSuggestions.AsNoTracking().SingleAsync(s => s.Id == suggestionId);
        stored.Status.Should().Be(AutoPoSuggestionStatus.Converted);
        stored.ConvertedPurchaseOrderId.Should().Be(poId);
        var po = await verify.PurchaseOrders.AsNoTracking().Include(p => p.Lines).SingleAsync(p => p.Id == poId);
        po.OriginSource.Should().Be(PoOriginSource.AutoMrp);
        po.Lines.Should().ContainSingle().Which.OrderedQuantity.Should().Be(12m);
    }
}
