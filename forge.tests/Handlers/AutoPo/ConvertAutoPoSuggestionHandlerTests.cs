using FluentAssertions;
using Moq;
using Forge.Api.Features.AutoPo;
using Forge.Core.Entities;
using Forge.Core.Enums;
using Forge.Core.Interfaces;
using Forge.Data.Context;
using Forge.Data.Repositories;
using Forge.Tests.Helpers;

namespace Forge.Tests.Handlers.AutoPo;

public class ConvertAutoPoSuggestionHandlerTests
{
    private readonly AppDbContext _db = TestDbContextFactory.Create();
    private readonly ConvertAutoPoSuggestionHandler _handler;

    public ConvertAutoPoSuggestionHandlerTests()
    {
        // Real repository over the InMemory context so the created PO gets a
        // key and the suggestion's ConvertedPurchaseOrderId link is real.
        _handler = new ConvertAutoPoSuggestionHandler(
            _db, new PurchaseOrderRepository(_db), Mock.Of<IBarcodeService>());
    }

    private async Task<AutoPoSuggestion> SeedSuggestionAsync()
    {
        var part = new Part { Id = 1, PartNumber = "P-001", Name = "Widget", Description = "Widget" };
        var vendor = new Vendor { Id = 2, CompanyName = "Acme Supply" };
        var suggestion = new AutoPoSuggestion
        {
            Id = 10,
            PartId = part.Id,
            VendorId = vendor.Id,
            SuggestedQty = 5,
            NeededByDate = DateTimeOffset.UtcNow.AddDays(14),
            Status = AutoPoSuggestionStatus.Pending,
        };
        _db.Parts.Add(part);
        _db.Vendors.Add(vendor);
        _db.AutoPoSuggestions.Add(suggestion);
        await _db.SaveChangesAsync();
        return suggestion;
    }

    [Fact]
    public async Task Handle_SetsAutoMrpOriginWithSuggestionReference()
    {
        // S4b provenance — converting an auto-PO suggestion stamps AutoMrp +
        // the suggestion reference + the converting user.
        var suggestion = await SeedSuggestionAsync();

        var poId = await _handler.Handle(
            new ConvertAutoPoSuggestionCommand(suggestion.Id, UserId: 42), CancellationToken.None);

        var po = await _db.PurchaseOrders.FindAsync(poId);
        po.Should().NotBeNull();
        po!.OriginSource.Should().Be(PoOriginSource.AutoMrp);
        po.OriginUserId.Should().Be(42);
        po.OriginReference.Should().Be($"Auto-PO suggestion #{suggestion.Id}");
    }

    [Fact]
    public async Task Handle_MarksSuggestionConvertedAndLinksPo()
    {
        var suggestion = await SeedSuggestionAsync();

        var poId = await _handler.Handle(
            new ConvertAutoPoSuggestionCommand(suggestion.Id, UserId: 42), CancellationToken.None);

        suggestion.Status.Should().Be(AutoPoSuggestionStatus.Converted);
        suggestion.ConvertedPurchaseOrderId.Should().Be(poId);
    }
}
