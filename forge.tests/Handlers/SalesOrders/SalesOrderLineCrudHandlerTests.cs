using FluentAssertions;
using MediatR;
using Moq;

using Forge.Api.Features.SalesOrders;
using Forge.Core.Entities;
using Forge.Core.Enums;
using Forge.Core.Interfaces;
using Forge.Core.Models;
using Forge.Tests.Helpers;

namespace Forge.Tests.Handlers.SalesOrders;

// SALES-LINE-CRUD: coverage for the new add/delete-line paths on sales orders.
public class SalesOrderLineCrudHandlerTests
{
    private readonly Mock<ISalesOrderRepository> _repo = new();
    private readonly Mock<IPartRepository> _partRepo = new();
    private readonly Mock<IMediator> _mediator = new();

    public SalesOrderLineCrudHandlerTests()
    {
        _partRepo
            .Setup(r => r.FindAsync(It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((int id, CancellationToken _) => new Part { Id = id, Status = PartStatus.Active });
        _mediator
            .Setup(m => m.Send(It.IsAny<GetSalesOrderByIdQuery>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((SalesOrderDetailResponseModel)null!);
    }

    private static SalesOrder DraftOrder(params SalesOrderLine[] lines)
    {
        var order = new SalesOrder { Id = 1, Status = SalesOrderStatus.Draft };
        foreach (var l in lines) order.Lines.Add(l);
        return order;
    }

    [Fact]
    public async Task AddLine_AppendsLine_WithNextLineNumber()
    {
        var order = DraftOrder(new SalesOrderLine { Id = 1, LineNumber = 1, Description = "Existing", Quantity = 1, UnitPrice = 10 });
        _repo.Setup(r => r.FindWithDetailsAsync(1, It.IsAny<CancellationToken>())).ReturnsAsync(order);
        using var db = TestDbContextFactory.Create();
        var handler = new AddSalesOrderLineHandler(_repo.Object, _partRepo.Object, db, _mediator.Object);

        await handler.Handle(new AddSalesOrderLineCommand(1, new CreateSalesOrderLineModel(7, "New part", 4, 25m, null)), default);

        order.Lines.Should().HaveCount(2);
        order.Lines.Single(l => l.Description == "New part").LineNumber.Should().Be(2);
        db.ActivityLogs.Local.Should().ContainSingle(a => a.Action == "line-added");
        _repo.Verify(r => r.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task AddLine_NonDraftOrder_Throws()
    {
        var order = new SalesOrder { Id = 1, Status = SalesOrderStatus.Confirmed };
        _repo.Setup(r => r.FindWithDetailsAsync(1, It.IsAny<CancellationToken>())).ReturnsAsync(order);
        using var db = TestDbContextFactory.Create();
        var handler = new AddSalesOrderLineHandler(_repo.Object, _partRepo.Object, db, _mediator.Object);

        var act = () => handler.Handle(new AddSalesOrderLineCommand(1, new CreateSalesOrderLineModel(null, "x", 1, 1m, null)), default);

        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("*draft*");
    }

    [Fact]
    public async Task DeleteLine_RemovesLine_WhenMoreThanOne()
    {
        var order = DraftOrder(
            new SalesOrderLine { Id = 1, LineNumber = 1, Description = "Keep", Quantity = 1, UnitPrice = 10 },
            new SalesOrderLine { Id = 2, LineNumber = 2, Description = "Drop", Quantity = 1, UnitPrice = 20 });
        _repo.Setup(r => r.FindWithDetailsAsync(1, It.IsAny<CancellationToken>())).ReturnsAsync(order);
        using var db = TestDbContextFactory.Create();
        var handler = new DeleteSalesOrderLineHandler(_repo.Object, db, _mediator.Object);

        await handler.Handle(new DeleteSalesOrderLineCommand(1, 2), default);

        order.Lines.Should().ContainSingle().Which.Id.Should().Be(1);
        db.ActivityLogs.Local.Should().ContainSingle(a => a.Action == "line-removed");
    }

    [Fact]
    public async Task DeleteLine_LastRemainingLine_Throws()
    {
        var order = DraftOrder(new SalesOrderLine { Id = 1, LineNumber = 1, Description = "Only", Quantity = 1, UnitPrice = 10 });
        _repo.Setup(r => r.FindWithDetailsAsync(1, It.IsAny<CancellationToken>())).ReturnsAsync(order);
        using var db = TestDbContextFactory.Create();
        var handler = new DeleteSalesOrderLineHandler(_repo.Object, db, _mediator.Object);

        var act = () => handler.Handle(new DeleteSalesOrderLineCommand(1, 1), default);

        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("*at least one line*");
    }
}
