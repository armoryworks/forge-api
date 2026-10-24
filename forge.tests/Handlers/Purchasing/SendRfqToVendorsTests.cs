using FluentAssertions;
using MediatR;
using Moq;

using Forge.Api.Features.Purchasing;
using Forge.Core.Entities;
using Forge.Core.Enums;
using Forge.Core.Interfaces;
using Forge.Core.Models;
using Forge.Data.Context;
using Forge.Data.Services;
using Forge.Tests.Helpers;

namespace Forge.Tests.Handlers.Purchasing;

public class SendRfqToVendorsTests
{
    private static readonly byte[] PdfBytes = [0x25, 0x50, 0x44, 0x46];
    private static readonly DateTimeOffset Now = new(2026, 10, 8, 16, 0, 0, TimeSpan.Zero);

    private readonly AppDbContext _db = TestDbContextFactory.Create();
    private readonly Mock<IIntegrationOutboxService> _outbox = new();
    private readonly Mock<IMediator> _mediator = new();
    private readonly SendRfqToVendorsHandler _handler;
    private readonly List<(string Key, EmailMessage Message, string? EntityType, int? EntityId)> _sent = [];

    public SendRfqToVendorsTests()
    {
        _outbox.Setup(o => o.EnqueueEmailAsync(
                It.IsAny<string>(), It.IsAny<EmailMessage>(), It.IsAny<string?>(), It.IsAny<int?>(), It.IsAny<CancellationToken>()))
            .Callback<string, EmailMessage, string?, int?, CancellationToken>((k, m, t, id, _) => _sent.Add((k, m, t, id)))
            .ReturnsAsync(new IntegrationOutboxEntry());
        _mediator.Setup(m => m.Send(It.IsAny<GetRfqPdfQuery>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(PdfBytes);
        var clock = new Mock<IClock>();
        clock.Setup(c => c.UtcNow).Returns(Now);
        var rfqService = new RfqService(_db, clock.Object, Mock.Of<IPurchaseOrderRepository>());
        _handler = new SendRfqToVendorsHandler(_db, rfqService, _outbox.Object, _mediator.Object);
    }

    private async Task SeedAsync(bool companyName = true)
    {
        _db.Parts.Add(new Part { Id = 3, PartNumber = "BRK-100", Name = "Bracket" });
        _db.Vendors.AddRange(
            new Vendor { Id = 4, CompanyName = "Mill Supply", ContactName = "Pat Buyer", Email = "quotes@mill.example" },
            new Vendor { Id = 5, CompanyName = "Coil Works", Email = " sales@coil.example " },
            new Vendor { Id = 6, CompanyName = "No Mail Metals", Email = null });
        _db.RequestForQuotes.Add(new RequestForQuote
        {
            Id = 9, RfqNumber = "RFQ-20261008-001", PartId = 3, Quantity = 250m,
            RequiredDate = Now.AddDays(30), ResponseDeadline = new DateTimeOffset(2026, 10, 15, 0, 0, 0, TimeSpan.Zero),
        });
        if (companyName)
            _db.SystemSettings.Add(new SystemSetting { Key = "company.name", Value = "Northwind Fabrication" });
        await _db.SaveChangesAsync();
    }

    [Fact]
    public async Task Queues_one_email_with_the_rfq_pdf_per_vendor_and_reports_vendors_without_email()
    {
        await SeedAsync();

        var result = await _handler.Handle(new SendRfqToVendorsCommand(9, [4, 5, 6]), CancellationToken.None);

        result.EmailedVendorIds.Should().Equal(4, 5);
        result.VendorsWithoutEmail.Should().ContainSingle()
            .Which.Should().Be(new RfqVendorWithoutEmailModel(6, "No Mail Metals"));
        _sent.Select(s => s.Message.To).Should().Equal("quotes@mill.example", "sales@coil.example");
        var first = _sent[0];
        first.Message.Subject.Should().Be("Request for Quote RFQ-20261008-001 from Northwind Fabrication");
        first.Message.HtmlBody.Should().Contain("Dear Pat Buyer").And.Contain("10/15/2026").And.Contain("BRK-100");
        first.Message.Attachments.Should().ContainSingle().Which.Should().Match<EmailAttachment>(a =>
            a.FileName == "RFQ-RFQ-20261008-001.pdf" && a.ContentType == "application/pdf" && a.Content == PdfBytes);
        first.EntityType.Should().Be("RequestForQuote");
        first.EntityId.Should().Be(9);
        first.Key.Should().Be("rfq-email:9:4:quotes@mill.example");
        _mediator.Verify(m => m.Send(It.Is<GetRfqPdfQuery>(q => q.Id == 9 && q.VendorId == 4), It.IsAny<CancellationToken>()), Times.Once);

        var rfq = _db.RequestForQuotes.Single(r => r.Id == 9);
        rfq.Status.Should().Be(RfqStatus.Sent);
        rfq.SentAt.Should().Be(Now);
        _db.RfqVendorResponses.Where(r => r.RfqId == 9).Select(r => r.VendorId).Should().BeEquivalentTo([4, 5]);
        _db.ActivityLogs.Should().ContainSingle(a => a.EntityType == "RequestForQuote" && a.EntityId == 9 && a.Action == "sent");
    }

    [Fact]
    public async Task Vendors_without_email_are_not_marked_sent_and_nothing_is_queued()
    {
        await SeedAsync();

        var result = await _handler.Handle(new SendRfqToVendorsCommand(9, [6]), CancellationToken.None);

        result.EmailedVendorIds.Should().BeEmpty();
        result.VendorsWithoutEmail.Select(v => v.VendorId).Should().Equal(6);
        _sent.Should().BeEmpty();
        _db.RequestForQuotes.Single(r => r.Id == 9).Status.Should().Be(RfqStatus.Draft);
        _db.RfqVendorResponses.Should().BeEmpty();
    }

    [Fact]
    public async Task A_missing_company_name_is_a_conflict_and_the_rfq_stays_unsent()
    {
        await SeedAsync(companyName: false);

        var act = () => _handler.Handle(new SendRfqToVendorsCommand(9, [4]), CancellationToken.None);

        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("Set your company name*");
        _sent.Should().BeEmpty();
        _db.RequestForQuotes.Single(r => r.Id == 9).Status.Should().Be(RfqStatus.Draft);
    }

    [Fact]
    public async Task An_unknown_vendor_is_not_found()
    {
        await SeedAsync();

        var act = () => _handler.Handle(new SendRfqToVendorsCommand(9, [4, 99]), CancellationToken.None);

        await act.Should().ThrowAsync<KeyNotFoundException>().WithMessage("Vendor 99 not found");
        _sent.Should().BeEmpty();
    }
}
