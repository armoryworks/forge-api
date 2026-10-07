using System.Text;

using FluentAssertions;
using MediatR;
using Microsoft.AspNetCore.Mvc;
using Moq;
using QuestPDF.Infrastructure;

using Forge.Api.Controllers;
using Forge.Api.Features.Quotes;
using Forge.Api.Services;
using Forge.Core.Entities;
using Forge.Core.Enums;
using Forge.Core.Interfaces;
using Forge.Data.Context;
using Forge.Integrations;
using Forge.Tests.Helpers;

namespace Forge.Tests.Handlers.Quotes;

public class GetQuotePdfTests
{
    private readonly AppDbContext _db = TestDbContextFactory.Create();
    private readonly Mock<ISystemSettingRepository> _settings = new();
    private readonly IClock _clock = new SystemClock();
    private readonly GetQuotePdfHandler _handler;

    public GetQuotePdfTests()
    {
        QuestPDF.Settings.License = LicenseType.Community;
        _handler = new GetQuotePdfHandler(_db, _settings.Object, new TermsCompilationService(_db, _clock));
    }

    private void SetCompanyName(string? value) =>
        _settings.Setup(s => s.FindByKeyAsync("company.name", It.IsAny<CancellationToken>()))
            .ReturnsAsync(value is null ? null : new SystemSetting { Key = "company.name", Value = value });

    private async Task<Quote> SeedQuoteAsync()
    {
        var customer = new Customer { Name = "Acme Corp" };
        _db.Customers.Add(customer);
        await _db.SaveChangesAsync();

        var quote = new Quote
        {
            CustomerId = customer.Id,
            QuoteNumber = "Q-2001",
            Status = QuoteStatus.Draft,
            Lines = { new QuoteLine { LineNumber = 1, Description = "Widget", Quantity = 2, UnitPrice = 15m } },
        };
        _db.Quotes.Add(quote);
        await _db.SaveChangesAsync();
        return quote;
    }

    [Fact]
    public async Task Handle_ReturnsPdfBytes_WithoutChangingStatus()
    {
        SetCompanyName("Northwind Fabrication");
        var quote = await SeedQuoteAsync();

        var bytes = await _handler.Handle(new GetQuotePdfQuery(quote.Id), CancellationToken.None);

        Encoding.ASCII.GetString(bytes, 0, 5).Should().Be("%PDF-");
        quote.Status.Should().Be(QuoteStatus.Draft);
        quote.SentDate.Should().BeNull();
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public async Task Handle_MissingCompanyName_StillRendersPdf(string? companyName)
    {
        SetCompanyName(companyName);
        var quote = await SeedQuoteAsync();

        var bytes = await _handler.Handle(new GetQuotePdfQuery(quote.Id), CancellationToken.None);

        Encoding.ASCII.GetString(bytes, 0, 5).Should().Be("%PDF-");
    }

    [Fact]
    public async Task Handle_UnknownQuote_ThrowsKeyNotFound()
    {
        SetCompanyName("Northwind Fabrication");

        var act = () => _handler.Handle(new GetQuotePdfQuery(999), CancellationToken.None);

        await act.Should().ThrowAsync<KeyNotFoundException>();
    }

    [Fact]
    public async Task Endpoint_ReturnsApplicationPdfFile()
    {
        var mediator = new Mock<IMediator>();
        var pdf = Encoding.ASCII.GetBytes("%PDF-1.7");
        mediator.Setup(m => m.Send(It.Is<GetQuotePdfQuery>(q => q.Id == 7), It.IsAny<CancellationToken>()))
            .ReturnsAsync(pdf);

        var result = await new QuotesController(mediator.Object).GetQuotePdf(7);

        var file = result.Should().BeOfType<FileContentResult>().Subject;
        file.ContentType.Should().Be("application/pdf");
        file.FileContents.Should().Equal(pdf);
        file.FileDownloadName.Should().Be("quote-7.pdf");
    }
}
