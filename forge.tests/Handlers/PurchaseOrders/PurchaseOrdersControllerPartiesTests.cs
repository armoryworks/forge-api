using FluentAssertions;
using MediatR;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Moq;

using Forge.Api.Controllers;
using Forge.Api.Features.PurchaseOrders;
using Forge.Core.Entities;
using Forge.Core.Enums;
using Forge.Core.Interfaces;
using Forge.Core.Models;
using Forge.Data.Context;
using Forge.Data.Repositories;
using Forge.Tests.Helpers;

namespace Forge.Tests.Handlers.PurchaseOrders;

public class PurchaseOrdersControllerPartiesTests
{
    private readonly AppDbContext _db = TestDbContextFactory.Create();
    private readonly Mock<IMediator> _mediator = new();
    private readonly PurchaseOrdersController _controller;

    public PurchaseOrdersControllerPartiesTests()
    {
        var poRepo = new PurchaseOrderRepository(_db);
        var vendorRepo = new Mock<IVendorRepository>();
        vendorRepo.Setup(r => r.FindAsync(It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .Returns((int id, CancellationToken ct) => _db.Vendors.FirstOrDefaultAsync(v => v.Id == id, ct));

        var createHandler = new CreatePurchaseOrderHandler(
            poRepo, vendorRepo.Object, Mock.Of<IPartRepository>(),
            Mock.Of<IBarcodeService>(),
            Mock.Of<ISystemSettingRepository>(),
            Mock.Of<IBusinessIdentifierService>(),
            Mock.Of<IMediator>(),
            Mock.Of<IHttpContextAccessor>(),
            _db,
            Mock.Of<IClock>());
        var updateHandler = new UpdatePurchaseOrderHandler(
            poRepo, Mock.Of<ISystemSettingRepository>(), Mock.Of<IBusinessIdentifierService>(), _db);

        _mediator.Setup(m => m.Send(It.IsAny<CreatePurchaseOrderCommand>(), It.IsAny<CancellationToken>()))
            .Returns((CreatePurchaseOrderCommand command, CancellationToken ct) => createHandler.Handle(command, ct));
        _mediator.Setup(m => m.Send(It.IsAny<UpdatePurchaseOrderCommand>(), It.IsAny<CancellationToken>()))
            .Returns((UpdatePurchaseOrderCommand command, CancellationToken ct) => updateHandler.Handle(command, ct));

        _controller = new PurchaseOrdersController(_mediator.Object);
    }

    private async Task SeedAsync()
    {
        _db.Vendors.Add(new Vendor { Id = 1, CompanyName = "Midwest Steel Supply" });
        _db.VendorContacts.AddRange(
            new VendorContact { Id = 10, VendorId = 1, FirstName = "Sam", LastName = "Sales" },
            new VendorContact { Id = 11, VendorId = 1, FirstName = "Pat", LastName = "Parts" },
            new VendorContact { Id = 12, VendorId = 1, FirstName = "Jane", LastName = "Doe", IsPrimary = true });
        _db.VendorAddresses.AddRange(
            new VendorAddress { Id = 31, VendorId = 1, Label = "Warehouse", AddressType = VendorAddressType.OrderFrom, Line1 = "2 Dock Rd", City = "Gary", State = "IN", PostalCode = "46403" },
            new VendorAddress { Id = 32, VendorId = 1, Label = "Sales office", AddressType = VendorAddressType.OrderFrom, IsDefault = true, Line1 = "3 Order Ave", City = "Gary", State = "IN", PostalCode = "46404" });
        _db.CompanyLocations.AddRange(
            new CompanyLocation { Id = 50, Name = "North Plant", Line1 = "10 North Rd", City = "Ogden", State = "UT", PostalCode = "84401" },
            new CompanyLocation { Id = 51, Name = "Main Plant", Line1 = "1 Factory Way", City = "Ogden", State = "UT", PostalCode = "84402", IsDefault = true });
        await _db.SaveChangesAsync();
    }

    private async Task<PurchaseOrder> ReloadAsync(int id)
    {
        _db.ChangeTracker.Clear();
        return await _db.PurchaseOrders.AsNoTracking().SingleAsync(p => p.Id == id);
    }

    [Fact]
    public async Task Create_keeps_the_contact_address_and_ship_to_chosen_over_the_vendor_defaults()
    {
        await SeedAsync();

        var result = await _controller.CreatePurchaseOrder(new CreatePurchaseOrderRequestModel(
            1, null, null, [new CreatePurchaseOrderLineModel(null, "Saw blade sharpening", 1, 60m, null)],
            VendorContactId: 10, VendorAddressId: 31, ShipToLocationId: 50));

        var created = (PurchaseOrderListItemModel)result.Result.Should().BeOfType<CreatedAtActionResult>().Subject.Value!;
        var po = await ReloadAsync(created.Id);
        po.VendorContactId.Should().Be(10);
        po.VendorAddressId.Should().Be(31);
        po.ShipToLocationId.Should().Be(50);
    }

    [Fact]
    public async Task Update_of_a_draft_keeps_the_contact_address_and_ship_to_chosen()
    {
        await SeedAsync();
        _db.PurchaseOrders.Add(new PurchaseOrder
        {
            Id = 7, PONumber = "PO-00007", VendorId = 1, Status = PurchaseOrderStatus.Draft,
            VendorContactId = 12, VendorAddressId = 32, ShipToLocationId = 51,
        });
        await _db.SaveChangesAsync();
        _db.ChangeTracker.Clear();

        var result = await _controller.UpdatePurchaseOrder(7, new UpdatePurchaseOrderRequestModel(
            null, null, VendorContactId: 11, VendorAddressId: 31, ShipToLocationId: 50));

        result.Should().BeOfType<NoContentResult>();
        var po = await ReloadAsync(7);
        po.VendorContactId.Should().Be(11);
        po.VendorAddressId.Should().Be(31);
        po.ShipToLocationId.Should().Be(50);
    }
}
