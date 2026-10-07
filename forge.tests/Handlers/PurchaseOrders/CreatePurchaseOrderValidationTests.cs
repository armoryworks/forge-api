using FluentAssertions;
using FluentValidation;
using Moq;
using Forge.Api.Features.PurchaseOrders;
using Forge.Core.Entities;
using Forge.Core.Enums;
using Forge.Core.Interfaces;
using Forge.Core.Models;
using Forge.Data.Context;
using Forge.Tests.Helpers;

namespace Forge.Tests.Handlers.PurchaseOrders;

public class CreatePurchaseOrderValidationTests
{
    private const int VendorId = 3;
    private const int PartId = 8;
    private static readonly DateTimeOffset Now = new(2026, 10, 7, 15, 30, 0, TimeSpan.Zero);

    private readonly Mock<IPurchaseOrderRepository> _poRepo = new();
    private readonly Mock<IVendorRepository> _vendorRepo = new();
    private readonly Mock<IPartRepository> _partRepo = new();
    private readonly Mock<IClock> _clock = new();
    private readonly AppDbContext _db = TestDbContextFactory.Create();
    private readonly CreatePurchaseOrderHandler _handler;
    private readonly CreatePurchaseOrderValidator _validator = new();

    public CreatePurchaseOrderValidationTests()
    {
        _clock.SetupGet(c => c.UtcNow).Returns(Now);
        _vendorRepo.Setup(r => r.FindAsync(VendorId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Vendor { Id = VendorId, CompanyName = "Vendor" });
        _partRepo.Setup(r => r.FindAsync(PartId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Part { Id = PartId, PartNumber = "P-008", Name = "Bracket" });
        _poRepo.Setup(r => r.GenerateNextPONumberAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync("PO-0100");

        _handler = new CreatePurchaseOrderHandler(
            _poRepo.Object, _vendorRepo.Object, _partRepo.Object,
            Mock.Of<IBarcodeService>(),
            Mock.Of<ISystemSettingRepository>(),
            Mock.Of<IBusinessIdentifierService>(),
            Mock.Of<MediatR.IMediator>(),
            Mock.Of<Microsoft.AspNetCore.Http.IHttpContextAccessor>(),
            _db,
            _clock.Object);
    }

    [Fact]
    public async Task Handle_JobDoesNotExist_ThrowsValidationErrorOnJobId()
    {
        var command = new CreatePurchaseOrderCommand(
            VendorId, 404, null,
            [new CreatePurchaseOrderLineModel(PartId, null, 1, 10m, null)]);

        var act = () => _handler.Handle(command, CancellationToken.None);

        var thrown = await act.Should().ThrowAsync<ValidationException>();
        thrown.Which.Errors.Should().ContainSingle(e => e.PropertyName == "jobId");
        _poRepo.Verify(r => r.AddAsync(It.IsAny<PurchaseOrder>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Handle_JobArchived_ThrowsValidationErrorOnJobId()
    {
        _db.Jobs.Add(new Job { Id = 42, JobNumber = "J-0042", Title = "Old bracket run", IsArchived = true });
        await _db.SaveChangesAsync();

        var command = new CreatePurchaseOrderCommand(
            VendorId, 42, null,
            [new CreatePurchaseOrderLineModel(PartId, null, 1, 10m, null)]);

        var act = () => _handler.Handle(command, CancellationToken.None);

        var thrown = await act.Should().ThrowAsync<ValidationException>();
        thrown.Which.Errors.Should().ContainSingle(e =>
            e.PropertyName == "jobId" && e.ErrorMessage.Contains("J-0042"));
    }

    [Fact]
    public async Task Handle_OpenJob_LinksPurchaseOrderToJob()
    {
        _db.Jobs.Add(new Job { Id = 1042, JobNumber = "J-1042", Title = "Bracket" });
        await _db.SaveChangesAsync();

        var command = new CreatePurchaseOrderCommand(
            VendorId, 1042, null,
            [new CreatePurchaseOrderLineModel(PartId, null, 1, 10m, null)]);

        await _handler.Handle(command, CancellationToken.None);

        _poRepo.Verify(r => r.AddAsync(It.Is<PurchaseOrder>(po => po.JobId == 1042), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public void Validator_PartlessLineWithDescription_IsValid()
    {
        var command = new CreatePurchaseOrderCommand(
            VendorId, null, null,
            [new CreatePurchaseOrderLineModel(null, "Heat treat per AMS 2759", 1, 250m, "Certs required")]);

        _validator.Validate(command).IsValid.Should().BeTrue();
    }

    [Fact]
    public void Validator_PartlessLineWithoutDescription_IsInvalid()
    {
        var command = new CreatePurchaseOrderCommand(
            VendorId, null, null,
            [new CreatePurchaseOrderLineModel(null, null, 1, 250m, null)]);

        _validator.Validate(command).Errors.Should().Contain(e => e.PropertyName.EndsWith("Description"));
    }

    [Fact]
    public void Validator_LineNoteOverLimit_IsInvalid()
    {
        var command = new CreatePurchaseOrderCommand(
            VendorId, null, null,
            [new CreatePurchaseOrderLineModel(null, "Heat treat", 1, 250m, new string('x', 1001))]);

        _validator.Validate(command).Errors.Should().Contain(e => e.PropertyName.EndsWith("Notes"));
    }

    [Fact]
    public async Task Handle_PartlessLine_PersistsDescriptionAndNote()
    {
        var command = new CreatePurchaseOrderCommand(
            VendorId, null, null,
            [new CreatePurchaseOrderLineModel(null, "Heat treat per AMS 2759", 1, 250m, "Certs required")]);

        await _handler.Handle(command, CancellationToken.None);

        _poRepo.Verify(r => r.AddAsync(It.Is<PurchaseOrder>(po =>
            po.Lines.Single().PartId == null
            && po.Lines.Single().Description == "Heat treat per AMS 2759"
            && po.Lines.Single().Notes == "Certs required"
            && po.ExpectedDeliveryDate == null
        ), It.IsAny<CancellationToken>()), Times.Once);
        _partRepo.Verify(r => r.FindAsync(It.IsAny<int>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Handle_NoExpectedDate_DefaultsFromVendorPartLeadTime()
    {
        _db.VendorParts.Add(new VendorPart { VendorId = VendorId, PartId = PartId, LeadTimeDays = 14, IsPreferred = true });
        await _db.SaveChangesAsync();

        var command = new CreatePurchaseOrderCommand(
            VendorId, null, null,
            [new CreatePurchaseOrderLineModel(PartId, null, 1, 10m, null)]);

        await _handler.Handle(command, CancellationToken.None);

        var expected = new DateTimeOffset(2026, 10, 21, 0, 0, 0, TimeSpan.Zero);
        _poRepo.Verify(r => r.AddAsync(It.Is<PurchaseOrder>(po => po.ExpectedDeliveryDate == expected), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Handle_ServiceLineFirst_DefaultsFromFirstPartLine()
    {
        _db.VendorParts.Add(new VendorPart
        {
            VendorId = VendorId, PartId = PartId, LeadTimeDays = 14, IsPreferred = true,
            Incoterm = Incoterm.DDP, Currency = "EUR",
        });
        await _db.SaveChangesAsync();

        var command = new CreatePurchaseOrderCommand(
            VendorId, null, null,
            [
                new CreatePurchaseOrderLineModel(null, "Setup charge", 1, 75m, null),
                new CreatePurchaseOrderLineModel(PartId, null, 1, 10m, null),
            ]);

        await _handler.Handle(command, CancellationToken.None);

        var expected = new DateTimeOffset(2026, 10, 21, 0, 0, 0, TimeSpan.Zero);
        _poRepo.Verify(r => r.AddAsync(It.Is<PurchaseOrder>(po =>
            po.ExpectedDeliveryDate == expected
            && po.Incoterm == Incoterm.DDP
            && po.QuoteCurrency == "EUR"), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Handle_ExpectedDateSupplied_KeepsCallerValue()
    {
        _db.VendorParts.Add(new VendorPart { VendorId = VendorId, PartId = PartId, LeadTimeDays = 14, IsPreferred = true });
        await _db.SaveChangesAsync();
        var supplied = new DateTimeOffset(2026, 11, 2, 0, 0, 0, TimeSpan.Zero);

        var command = new CreatePurchaseOrderCommand(
            VendorId, null, null,
            [new CreatePurchaseOrderLineModel(PartId, null, 1, 10m, null)],
            ExpectedDeliveryDate: supplied);

        await _handler.Handle(command, CancellationToken.None);

        _poRepo.Verify(r => r.AddAsync(It.Is<PurchaseOrder>(po => po.ExpectedDeliveryDate == supplied), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Handle_ValidCommand_WritesCreatedActivity()
    {
        var command = new CreatePurchaseOrderCommand(
            VendorId, null, null,
            [new CreatePurchaseOrderLineModel(PartId, null, 1, 10m, null)]);

        await _handler.Handle(command, CancellationToken.None);

        _db.ActivityLogs.Should().ContainSingle(a =>
            a.EntityType == "PurchaseOrder" && a.Action == "created" && a.Description.Contains("PO-0100"));
    }
}
