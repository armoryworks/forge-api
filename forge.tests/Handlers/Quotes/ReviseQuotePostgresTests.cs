using FluentAssertions;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Moq;

using Forge.Api.Features.Quotes;
using Forge.Api.Features.SalesTax;
using Forge.Api.Services;
using Forge.Core.Entities;
using Forge.Core.Enums;
using Forge.Core.Interfaces;
using Forge.Core.Models;
using Forge.Data.Context;
using Forge.Data.Repositories;
using Forge.Tests.Helpers;

namespace Forge.Tests.Handlers.Quotes;

[Collection(PostgresCollection.Name)]
public sealed class ReviseQuotePostgresTests(PostgresFixture fixture)
{
    private static readonly DateTimeOffset Now = new(2026, 10, 23, 16, 38, 0, TimeSpan.Zero);

    private static DuplicateQuoteHandler Handler(AppDbContext db)
    {
        var clock = Mock.Of<IClock>(c => c.UtcNow == Now);
        var taxMediator = new Mock<IMediator>();
        taxMediator.Setup(m => m.Send(It.IsAny<GetTaxRateForCustomerQuery>(), It.IsAny<CancellationToken>()))
            .Returns((IRequest<SalesTaxRateResponseModel?> q, CancellationToken ct) =>
                new GetTaxRateForCustomerHandler(db, clock).Handle((GetTaxRateForCustomerQuery)q, ct));
        var repo = new QuoteRepository(db);
        var mediator = new Mock<IMediator>();
        mediator.Setup(m => m.Send(It.IsAny<GetQuoteByIdQuery>(), It.IsAny<CancellationToken>()))
            .Returns((IRequest<QuoteDetailResponseModel> q, CancellationToken ct) =>
                new GetQuoteByIdHandler(repo).Handle((GetQuoteByIdQuery)q, ct));
        return new DuplicateQuoteHandler(
            db, repo, mediator.Object, new TaxOverrideGuard(db, taxMediator.Object, clock), Mock.Of<IBusinessIdentifierService>());
    }

    [Fact]
    public async Task A_sent_quote_converted_from_an_estimate_with_the_inherited_rate_can_be_revised()
    {
        var tag = Guid.NewGuid().ToString("N")[..8];
        await using var db = fixture.CreateContext();
        var rate = new SalesTaxRate
        {
            Name = $"County {tag}", Code = $"T-{tag}", StateCode = "UT", Rate = 0.0725m,
            EffectiveFrom = Now.AddMonths(-10), IsActive = true,
        };
        db.SalesTaxRates.Add(rate);
        await db.SaveChangesAsync();
        var customer = new Customer { Name = $"Revise Co {tag}", DefaultTaxCodeId = rate.Id };
        db.Customers.Add(customer);
        await db.SaveChangesAsync();
        var estimate = new Quote { Type = QuoteType.Estimate, CustomerId = customer.Id, Title = "Manifold", Status = QuoteStatus.ConvertedToQuote };
        db.Quotes.Add(estimate);
        await db.SaveChangesAsync();
        var quote = new Quote
        {
            Type = QuoteType.Quote, QuoteNumber = $"QP-{tag}", CustomerId = customer.Id, Status = QuoteStatus.Sent,
            TaxRate = 0.0725m, SourceEstimateId = estimate.Id, SentDate = Now.AddMinutes(-3),
        };
        quote.Lines.Add(new QuoteLine { LineNumber = 1, Description = "Manifold body", Quantity = 250m, UnitPrice = 96m });
        db.Quotes.Add(quote);
        await db.SaveChangesAsync();

        var revision = await Handler(db).Handle(new DuplicateQuoteCommand(quote.Id, AsRevision: true), CancellationToken.None);

        await using var verify = fixture.CreateContext();
        var stored = await verify.Quotes.AsNoTracking().Where(q => q.Id == quote.Id || q.Id == revision.Id).ToListAsync();
        stored.Single(q => q.Id == quote.Id).Status.Should().Be(QuoteStatus.Superseded);
        stored.Single(q => q.Id == quote.Id).SourceEstimateId.Should().BeNull();
        var revised = stored.Single(q => q.Id == revision.Id);
        revised.QuoteNumber.Should().Be($"QP-{tag}-R1");
        revised.SourceEstimateId.Should().Be(estimate.Id);
        revised.TaxRate.Should().Be(0.0725m);
        revised.TaxDocumentId.Should().BeNull();
    }
}
