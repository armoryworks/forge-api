using FluentAssertions;
using FluentValidation;
using MediatR;
using Moq;

using Forge.Api.Features.PurchaseOrders;
using Forge.Core.Entities;
using Forge.Core.Enums;
using Forge.Core.Interfaces;
using Forge.Core.Models;
using Forge.Data.Context;
using Forge.Tests.Helpers;

namespace Forge.Tests.Handlers.PurchaseOrders;

public class SendPurchaseOrderEmailTests
{
    private static readonly byte[] PdfBytes = [0x25, 0x50, 0x44, 0x46];
    private static readonly DateTimeOffset Now = new(2026, 10, 8, 16, 0, 0, TimeSpan.Zero);

    private readonly AppDbContext _db = TestDbContextFactory.Create();
    private readonly Mock<IIntegrationOutboxService> _outbox = new();
    private readonly Mock<IMediator> _mediator = new();
    private readonly SendPurchaseOrderEmailHandler _handler;
    private readonly List<(string Key, EmailMessage Message, string? EntityType, int? EntityId)> _sent = [];

    public SendPurchaseOrderEmailTests()
    {
        _outbox.Setup(o => o.EnqueueEmailAsync(
                It.IsAny<string>(), It.IsAny<EmailMessage>(), It.IsAny<string?>(), It.IsAny<int?>(), It.IsAny<CancellationToken>()))
            .Callback<string, EmailMessage, string?, int?, CancellationToken>((k, m, t, id, _) => _sent.Add((k, m, t, id)))
            .ReturnsAsync(new IntegrationOutboxEntry());
        _mediator.Setup(m => m.Send(It.Is<GetPurchaseOrderPdfQuery>(q => q.Id == 12), It.IsAny<CancellationToken>()))
            .ReturnsAsync(PdfBytes);
        var clock = new Mock<IClock>();
        clock.Setup(c => c.UtcNow).Returns(Now);
        _handler = new SendPurchaseOrderEmailHandler(_db, _outbox.Object, clock.Object, _mediator.Object);
    }

    private async Task SeedAsync(PurchaseOrderStatus status = PurchaseOrderStatus.Submitted, string? vendorEmail = "orders@mill.example", bool companyName = true)
    {
        _db.Vendors.Add(new Vendor { Id = 4, CompanyName = "Mill Supply", ContactName = "Pat Buyer", Email = vendorEmail });
        _db.PurchaseOrders.Add(new PurchaseOrder
        {
            Id = 12, PONumber = "PO-00012", VendorId = 4, Status = status,
            ExpectedDeliveryDate = new DateTimeOffset(2026, 10, 20, 0, 0, 0, TimeSpan.Zero),
        });
        if (companyName)
            _db.SystemSettings.Add(new SystemSetting { Key = "company.name", Value = "Northwind Fabrication" });
        await _db.SaveChangesAsync();
    }

    private IQueryable<ActivityLog> EmailActivity() => _db.ActivityLogs.Where(a => a.Action == "emailed");

    [Fact]
    public async Task Sends_the_po_pdf_to_the_vendor_email_by_default_and_logs_it()
    {
        await SeedAsync();

        await _handler.Handle(new SendPurchaseOrderEmailCommand(12, null, null, null), CancellationToken.None);

        var sent = _sent.Should().ContainSingle().Subject;
        sent.Message.To.Should().Be("orders@mill.example");
        sent.Message.Cc.Should().BeNull();
        sent.Message.Subject.Should().Be("Purchase Order PO-00012 from Northwind Fabrication");
        sent.Message.HtmlBody.Should().Contain("Dear Pat Buyer").And.Contain("10/20/2026");
        sent.Message.Attachments.Should().ContainSingle().Which.Should().Match<EmailAttachment>(a =>
            a.FileName == "PurchaseOrder-PO-00012.pdf" && a.ContentType == "application/pdf" && a.Content == PdfBytes);
        sent.EntityType.Should().Be("PurchaseOrder");
        sent.EntityId.Should().Be(12);
        sent.Key.Should().Be($"po-email:12:orders@mill.example:{Now.ToUnixTimeSeconds()}");
        EmailActivity().Should().ContainSingle().Which.Should().Match<ActivityLog>(a =>
            a.EntityType == "PurchaseOrder" && a.EntityId == 12 && a.Action == "emailed"
            && a.Description == "Emailed to orders@mill.example");
    }

    [Fact]
    public async Task An_explicit_recipient_cc_list_and_message_are_used()
    {
        await SeedAsync();

        await _handler.Handle(new SendPurchaseOrderEmailCommand(
            12, " sales@mill.example ", "ap@shop.example; buyer@shop.example, ap@shop.example", "Please <rush> this"),
            CancellationToken.None);

        var message = _sent.Should().ContainSingle().Subject.Message;
        message.To.Should().Be("sales@mill.example");
        message.Cc.Should().Equal("ap@shop.example", "buyer@shop.example");
        message.HtmlBody.Should().Contain("Please &lt;rush&gt; this");
        EmailActivity().Should().ContainSingle().Which.Description
            .Should().Be("Emailed to sales@mill.example, cc ap@shop.example, buyer@shop.example");
    }

    [Fact]
    public async Task A_missing_company_name_is_a_conflict_and_nothing_is_sent()
    {
        await SeedAsync(companyName: false);

        var act = () => _handler.Handle(new SendPurchaseOrderEmailCommand(12, null, null, null), CancellationToken.None);

        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("Set your company name*");
        _sent.Should().BeEmpty();
        EmailActivity().Should().BeEmpty();
    }

    [Fact]
    public async Task A_vendor_without_an_email_needs_an_explicit_recipient()
    {
        await SeedAsync(vendorEmail: null);

        var act = () => _handler.Handle(new SendPurchaseOrderEmailCommand(12, null, null, null), CancellationToken.None);

        (await act.Should().ThrowAsync<ValidationException>()).Which.Errors.Should().ContainSingle(e => e.PropertyName == "to");
        _sent.Should().BeEmpty();
    }

    [Fact]
    public async Task A_cancelled_po_cannot_be_emailed()
    {
        await SeedAsync(PurchaseOrderStatus.Cancelled);

        var act = () => _handler.Handle(new SendPurchaseOrderEmailCommand(12, null, null, null), CancellationToken.None);

        await act.Should().ThrowAsync<InvalidOperationException>();
        _sent.Should().BeEmpty();
    }

    [Fact]
    public async Task An_unknown_po_is_not_found()
    {
        var act = () => _handler.Handle(new SendPurchaseOrderEmailCommand(12, null, null, null), CancellationToken.None);

        await act.Should().ThrowAsync<KeyNotFoundException>();
    }

    [Theory]
    [InlineData(null, null, true)]
    [InlineData("buyer@mill.example", "ap@shop.example; qa@shop.example", true)]
    [InlineData("not-an-address", null, false)]
    [InlineData(null, "ap@shop.example, nope", false)]
    public void Recipients_are_validated(string? to, string? cc, bool valid)
    {
        new SendPurchaseOrderEmailValidator()
            .Validate(new SendPurchaseOrderEmailCommand(12, to, cc, null))
            .IsValid.Should().Be(valid);
    }
}
