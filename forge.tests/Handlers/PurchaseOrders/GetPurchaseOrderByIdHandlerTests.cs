using FluentAssertions;
using Moq;

using Forge.Api.Features.PurchaseOrders;
using Forge.Core.Entities;
using Forge.Core.Interfaces;
using Forge.Tests.Helpers;

namespace Forge.Tests.Handlers.PurchaseOrders;

public class GetPurchaseOrderByIdHandlerTests
{
    private readonly Mock<IPurchaseOrderRepository> _repo = new();
    private readonly GetPurchaseOrderByIdHandler _handler;

    public GetPurchaseOrderByIdHandlerTests()
    {
        _handler = new GetPurchaseOrderByIdHandler(_repo.Object, TestDbContextFactory.Create());
    }

    [Fact]
    public async Task Handle_PartlessLine_ReturnsLineWithNullPartNumber()
    {
        var po = new PurchaseOrder
        {
            Id = 7,
            PONumber = "PO-0007",
            VendorId = 3,
            Vendor = new Vendor { Id = 3, CompanyName = "Vendor" },
        };
        po.Lines.Add(new PurchaseOrderLine
        {
            Id = 70,
            PartId = 8,
            Part = new Part { Id = 8, PartNumber = "P-008", Name = "Bracket" },
            Description = "Bracket",
            OrderedQuantity = 2,
            UnitPrice = 10m,
        });
        po.Lines.Add(new PurchaseOrderLine
        {
            Id = 71,
            PartId = null,
            Description = "Heat treat service",
            OrderedQuantity = 1,
            UnitPrice = 250m,
            Notes = "Certs required",
        });
        _repo.Setup(r => r.FindWithDetailsAsync(7, It.IsAny<CancellationToken>())).ReturnsAsync(po);

        var result = await _handler.Handle(new GetPurchaseOrderByIdQuery(7), CancellationToken.None);

        result.Lines.Should().HaveCount(2);
        result.Lines.Single(l => l.Id == 70).PartNumber.Should().Be("P-008");
        var service = result.Lines.Single(l => l.Id == 71);
        service.PartId.Should().BeNull();
        service.PartNumber.Should().BeNull();
        service.Description.Should().Be("Heat treat service");
        service.LineTotal.Should().Be(250m);
    }
}
