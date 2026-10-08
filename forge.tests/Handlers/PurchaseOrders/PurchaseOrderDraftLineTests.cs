using FluentAssertions;
using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Moq;

using Forge.Api.Features.PurchaseOrders;
using Forge.Core.Entities;
using Forge.Core.Enums;
using Forge.Core.Interfaces;
using Forge.Core.Models;
using Forge.Data.Context;
using Forge.Data.Repositories;
using Forge.Tests.Helpers;

namespace Forge.Tests.Handlers.PurchaseOrders;

public class PurchaseOrderDraftLineTests
{
    private readonly AppDbContext _db = TestDbContextFactory.Create();
    private readonly Mock<IPartRepository> _parts = new();
    private readonly Mock<IMediator> _mediator = new();
    private readonly AddPurchaseOrderLineHandler _add;
    private readonly DeletePurchaseOrderLineHandler _delete;

    public PurchaseOrderDraftLineTests()
    {
        var repo = new PurchaseOrderRepository(_db);
        _add = new AddPurchaseOrderLineHandler(repo, _parts.Object, _db, _mediator.Object);
        _delete = new DeletePurchaseOrderLineHandler(repo, _db, _mediator.Object);
    }

    private async Task<PurchaseOrder> SeedAsync(PurchaseOrderStatus status, int lineCount = 2)
    {
        _db.Vendors.Add(new Vendor { Id = 3, CompanyName = "Mill Supply" });
        var bar = new Part { Id = 20, PartNumber = "BAR-1018", Name = "1018 bar", Description = "1018 cold-rolled bar" };
        _db.Parts.Add(bar);
        var po = new PurchaseOrder { Id = 5, PONumber = "PO-00005", VendorId = 3, Status = status };
        for (var i = 1; i <= lineCount; i++)
            po.Lines.Add(new PurchaseOrderLine
            {
                Id = i, PartId = i == 1 ? 20 : null, Description = i == 1 ? "1018 cold-rolled bar" : $"Saw service {i}",
                OrderedQuantity = 10m, UnitPrice = 3m,
            });
        _db.PurchaseOrders.Add(po);
        await _db.SaveChangesAsync();
        _parts.Setup(p => p.FindAsync(20, It.IsAny<CancellationToken>())).ReturnsAsync(bar);
        return po;
    }

    private IQueryable<ActivityLog> LineActivity() =>
        _db.ActivityLogs.Where(a => a.Action == "line-added" || a.Action == "line-removed");

    private async Task<List<PurchaseOrderLine>> LinesAsync() =>
        await _db.PurchaseOrderLines.AsNoTracking().Where(l => l.PurchaseOrderId == 5).OrderBy(l => l.Id).ToListAsync();

    [Fact]
    public async Task A_part_line_added_to_a_draft_takes_the_part_description_and_is_logged()
    {
        await SeedAsync(PurchaseOrderStatus.Draft);

        await _add.Handle(new AddPurchaseOrderLineCommand(5,
            new AddPurchaseOrderLineRequestModel(20, null, 4m, 2.5m, "Cut to 12in")), CancellationToken.None);

        var added = (await LinesAsync()).Should().HaveCount(3).And.Subject.Last();
        added.PartId.Should().Be(20);
        added.Description.Should().Be("1018 cold-rolled bar");
        added.OrderedQuantity.Should().Be(4m);
        added.UnitPrice.Should().Be(2.5m);
        added.Notes.Should().Be("Cut to 12in");
        (await LineActivity().SingleAsync()).Should().Match<ActivityLog>(a =>
            a.EntityType == "PurchaseOrder" && a.EntityId == 5 && a.Action == "line-added"
            && a.Description == "Added line BAR-1018: 4 @ 2.50");
        _mediator.Verify(m => m.Send(It.Is<GetPurchaseOrderByIdQuery>(q => q.Id == 5), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task A_non_stock_line_keeps_its_own_description()
    {
        await SeedAsync(PurchaseOrderStatus.Draft);

        await _add.Handle(new AddPurchaseOrderLineCommand(5,
            new AddPurchaseOrderLineRequestModel(null, "  Anodize, type II  ", 1m, 80m, null)), CancellationToken.None);

        var added = (await LinesAsync()).Last();
        added.PartId.Should().BeNull();
        added.Description.Should().Be("Anodize, type II");
        (await LineActivity().SingleAsync()).Description.Should().Be("Added line Anodize, type II: 1 @ 80.00");
    }

    [Theory]
    [InlineData(PurchaseOrderStatus.Submitted)]
    [InlineData(PurchaseOrderStatus.Acknowledged)]
    [InlineData(PurchaseOrderStatus.Cancelled)]
    public async Task Lines_cannot_be_added_outside_draft(PurchaseOrderStatus status)
    {
        await SeedAsync(status);

        var act = () => _add.Handle(new AddPurchaseOrderLineCommand(5,
            new AddPurchaseOrderLineRequestModel(null, "Freight", 1m, 10m, null)), CancellationToken.None);

        await act.Should().ThrowAsync<InvalidOperationException>();
        (await LinesAsync()).Should().HaveCount(2);
        LineActivity().Should().BeEmpty();
    }

    [Fact]
    public async Task An_inactive_part_cannot_be_added()
    {
        await SeedAsync(PurchaseOrderStatus.Draft);
        _parts.Setup(p => p.FindAsync(21, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Part { Id = 21, PartNumber = "OLD-1", Name = "Old", Status = PartStatus.Obsolete });

        var act = () => _add.Handle(new AddPurchaseOrderLineCommand(5,
            new AddPurchaseOrderLineRequestModel(21, null, 1m, 1m, null)), CancellationToken.None);

        await act.Should().ThrowAsync<ValidationException>();
    }

    [Theory]
    [InlineData(null, null, 1, 1, false)]
    [InlineData(null, "Service", 0, 1, false)]
    [InlineData(null, "Service", 1, -1, false)]
    [InlineData(20, null, 1, 0, true)]
    [InlineData(null, "Service", 0.5, 0, true)]
    public void Add_validation_matches_create_line_rules(int? partId, string? description, double quantity, double unitPrice, bool valid)
    {
        var result = new AddPurchaseOrderLineValidator().Validate(new AddPurchaseOrderLineCommand(5,
            new AddPurchaseOrderLineRequestModel(partId, description, (decimal)quantity, (decimal)unitPrice, null)));

        result.IsValid.Should().Be(valid);
    }

    [Fact]
    public async Task A_draft_line_can_be_removed_and_the_removal_is_logged()
    {
        await SeedAsync(PurchaseOrderStatus.Draft);

        await _delete.Handle(new DeletePurchaseOrderLineCommand(5, 1), CancellationToken.None);

        (await LinesAsync()).Should().ContainSingle().Which.Id.Should().Be(2);
        (await LineActivity().SingleAsync()).Should().Match<ActivityLog>(a =>
            a.EntityType == "PurchaseOrder" && a.EntityId == 5 && a.Action == "line-removed"
            && a.Description == "Removed line BAR-1018");
        _mediator.Verify(m => m.Send(It.Is<GetPurchaseOrderByIdQuery>(q => q.Id == 5), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Lines_cannot_be_removed_outside_draft()
    {
        await SeedAsync(PurchaseOrderStatus.Submitted);

        var act = () => _delete.Handle(new DeletePurchaseOrderLineCommand(5, 1), CancellationToken.None);

        await act.Should().ThrowAsync<InvalidOperationException>();
        (await LinesAsync()).Should().HaveCount(2);
    }

    [Fact]
    public async Task The_last_line_cannot_be_removed()
    {
        await SeedAsync(PurchaseOrderStatus.Draft, lineCount: 1);

        var act = () => _delete.Handle(new DeletePurchaseOrderLineCommand(5, 1), CancellationToken.None);

        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("A purchase order needs at least one line*");
        (await LinesAsync()).Should().ContainSingle();
    }

    [Fact]
    public async Task A_line_with_a_release_cannot_be_removed()
    {
        await SeedAsync(PurchaseOrderStatus.Draft);
        _db.PurchaseOrderReleases.Add(new PurchaseOrderRelease { PurchaseOrderId = 5, PurchaseOrderLineId = 2, ReleaseNumber = 1, Quantity = 2m });
        await _db.SaveChangesAsync();

        var act = () => _delete.Handle(new DeletePurchaseOrderLineCommand(5, 2), CancellationToken.None);

        await act.Should().ThrowAsync<InvalidOperationException>();
        (await LinesAsync()).Should().HaveCount(2);
    }

    [Fact]
    public async Task Removing_a_line_from_another_order_is_not_found()
    {
        await SeedAsync(PurchaseOrderStatus.Draft);

        var act = () => _delete.Handle(new DeletePurchaseOrderLineCommand(5, 99), CancellationToken.None);

        await act.Should().ThrowAsync<KeyNotFoundException>();
    }
}
