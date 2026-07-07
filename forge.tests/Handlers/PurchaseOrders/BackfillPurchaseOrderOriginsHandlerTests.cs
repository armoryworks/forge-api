using FluentAssertions;
using Microsoft.Extensions.Logging;
using Moq;
using Forge.Api.Features.PurchaseOrders;
using Forge.Core.Entities;
using Forge.Core.Enums;
using Forge.Data.Context;
using Forge.Tests.Helpers;

namespace Forge.Tests.Handlers.PurchaseOrders;

public class BackfillPurchaseOrderOriginsHandlerTests
{
    private readonly AppDbContext _db = TestDbContextFactory.Create();
    private readonly BackfillPurchaseOrderOriginsHandler _handler;

    public BackfillPurchaseOrderOriginsHandlerTests()
    {
        _handler = new BackfillPurchaseOrderOriginsHandler(
            _db, Mock.Of<ILogger<BackfillPurchaseOrderOriginsHandler>>());
    }

    private async Task SeedAsync()
    {
        _db.Vendors.Add(new Vendor { Id = 1, CompanyName = "Acme Supply" });
        _db.Parts.Add(new Part { Id = 1, PartNumber = "P-001", Name = "Widget" });

        // 1 — external ingestion (Provider stamped by accounting sync).
        _db.PurchaseOrders.Add(new PurchaseOrder
        {
            Id = 101, PONumber = "PO-00101", VendorId = 1, Provider = "QuickBooks",
        });

        // 2 — converted auto-PO suggestion.
        _db.PurchaseOrders.Add(new PurchaseOrder { Id = 102, PONumber = "PO-00102", VendorId = 1 });
        _db.AutoPoSuggestions.Add(new AutoPoSuggestion
        {
            Id = 55, PartId = 1, VendorId = 1, SuggestedQty = 5,
            NeededByDate = DateTimeOffset.UtcNow,
            Status = AutoPoSuggestionStatus.Converted,
            ConvertedPurchaseOrderId = 102,
        });

        // 3 — Provider wins over the suggestion match (precedence).
        _db.PurchaseOrders.Add(new PurchaseOrder
        {
            Id = 103, PONumber = "PO-00103", VendorId = 1, Provider = "Xero",
        });
        _db.AutoPoSuggestions.Add(new AutoPoSuggestion
        {
            Id = 56, PartId = 1, VendorId = 1, SuggestedQty = 2,
            NeededByDate = DateTimeOffset.UtcNow,
            Status = AutoPoSuggestionStatus.Converted,
            ConvertedPurchaseOrderId = 103,
        });

        // 4 — genuinely manual: stays Manual.
        _db.PurchaseOrders.Add(new PurchaseOrder { Id = 104, PONumber = "PO-00104", VendorId = 1 });

        // 5 — already classified by a creation path: NOT a candidate.
        _db.PurchaseOrders.Add(new PurchaseOrder
        {
            Id = 105, PONumber = "PO-00105", VendorId = 1,
            OriginSource = PoOriginSource.AutoQuote, OriginReference = "RFQ-20260101-001",
        });

        // 6 — Manual with an origin user already stamped: NOT a candidate.
        _db.PurchaseOrders.Add(new PurchaseOrder
        {
            Id = 106, PONumber = "PO-00106", VendorId = 1, OriginUserId = 42,
        });

        await _db.SaveChangesAsync();
    }

    [Fact]
    public async Task Handle_ClassifiesExternalThenAutoMrpAndLeavesRestManual()
    {
        await SeedAsync();

        var result = await _handler.Handle(
            new BackfillPurchaseOrderOriginsCommand(), CancellationToken.None);

        result.ExternalIntegrationCount.Should().Be(2);   // 101 + 103 (Provider precedence)
        result.AutoMrpCount.Should().Be(1);               // 102
        result.RemainingManualCount.Should().Be(1);       // 104

        (await _db.PurchaseOrders.FindAsync(101))!.OriginSource.Should().Be(PoOriginSource.ExternalIntegration);
        (await _db.PurchaseOrders.FindAsync(101))!.OriginReference.Should().Be("QuickBooks");
        (await _db.PurchaseOrders.FindAsync(102))!.OriginSource.Should().Be(PoOriginSource.AutoMrp);
        (await _db.PurchaseOrders.FindAsync(102))!.OriginReference.Should().Be("Auto-PO suggestion #55");
        (await _db.PurchaseOrders.FindAsync(103))!.OriginSource.Should().Be(PoOriginSource.ExternalIntegration);
        (await _db.PurchaseOrders.FindAsync(103))!.OriginReference.Should().Be("Xero");
        (await _db.PurchaseOrders.FindAsync(104))!.OriginSource.Should().Be(PoOriginSource.Manual);
        (await _db.PurchaseOrders.FindAsync(104))!.OriginReference.Should().BeNull();
    }

    [Fact]
    public async Task Handle_DoesNotTouchAlreadyClassifiedRows()
    {
        await SeedAsync();

        await _handler.Handle(new BackfillPurchaseOrderOriginsCommand(), CancellationToken.None);

        var alreadyClassified = await _db.PurchaseOrders.FindAsync(105);
        alreadyClassified!.OriginSource.Should().Be(PoOriginSource.AutoQuote);
        alreadyClassified.OriginReference.Should().Be("RFQ-20260101-001");

        var userStamped = await _db.PurchaseOrders.FindAsync(106);
        userStamped!.OriginSource.Should().Be(PoOriginSource.Manual);
        userStamped.OriginUserId.Should().Be(42);
        userStamped.OriginReference.Should().BeNull();
    }

    [Fact]
    public async Task Handle_IsIdempotent_SecondRunUpdatesNothing()
    {
        await SeedAsync();

        await _handler.Handle(new BackfillPurchaseOrderOriginsCommand(), CancellationToken.None);
        var second = await _handler.Handle(
            new BackfillPurchaseOrderOriginsCommand(), CancellationToken.None);

        second.ExternalIntegrationCount.Should().Be(0);
        second.AutoMrpCount.Should().Be(0);
        second.RemainingManualCount.Should().Be(1);       // 104 remains the only candidate

        (await _db.PurchaseOrders.FindAsync(102))!.OriginReference.Should().Be("Auto-PO suggestion #55");
    }
}
