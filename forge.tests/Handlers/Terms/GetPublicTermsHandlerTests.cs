using FluentAssertions;

using Forge.Api.Features.Terms;
using Forge.Core.Entities;
using Forge.Data.Context;
using Forge.Tests.Helpers;

namespace Forge.Tests.Handlers.Terms;

public class GetPublicTermsHandlerTests
{
    private readonly AppDbContext _db = TestDbContextFactory.Create();

    [Fact]
    public async Task Handle_KnownToken_ReturnsSnapshotHtmlAndQuoteNumber()
    {
        var customer = new Customer { Name = "Acme" };
        _db.Customers.Add(customer);
        await _db.SaveChangesAsync();

        var quote = new Quote { CustomerId = customer.Id, QuoteNumber = "Q-2001" };
        _db.Quotes.Add(quote);
        await _db.SaveChangesAsync();

        _db.QuoteTermsSnapshots.Add(new QuoteTermsSnapshot
        {
            QuoteId = quote.Id,
            CompiledHtml = "<section><h2>Standard Terms</h2><p>All sales are final.</p></section>",
            AccessToken = "known-token",
            SentTo = "buyer@acme.test",
        });
        await _db.SaveChangesAsync();

        var handler = new GetPublicTermsHandler(_db);
        var result = await handler.Handle(new GetPublicTermsQuery("known-token"), CancellationToken.None);

        result.QuoteNumber.Should().Be("Q-2001");
        result.CompiledHtml.Should().Contain("All sales are final.");
    }

    [Fact]
    public async Task Handle_UnknownToken_ThrowsKeyNotFound()
    {
        var handler = new GetPublicTermsHandler(_db);
        var act = () => handler.Handle(new GetPublicTermsQuery("nope"), CancellationToken.None);

        await act.Should().ThrowAsync<KeyNotFoundException>();
    }

    [Fact]
    public async Task Handle_SoftDeletedSnapshot_ThrowsKeyNotFound()
    {
        var customer = new Customer { Name = "Acme" };
        _db.Customers.Add(customer);
        await _db.SaveChangesAsync();

        var quote = new Quote { CustomerId = customer.Id, QuoteNumber = "Q-2002" };
        _db.Quotes.Add(quote);
        await _db.SaveChangesAsync();

        _db.QuoteTermsSnapshots.Add(new QuoteTermsSnapshot
        {
            QuoteId = quote.Id,
            CompiledHtml = "<p>x</p>",
            AccessToken = "deleted-token",
            DeletedAt = DateTimeOffset.UtcNow,
        });
        await _db.SaveChangesAsync();

        var handler = new GetPublicTermsHandler(_db);
        var act = () => handler.Handle(new GetPublicTermsQuery("deleted-token"), CancellationToken.None);

        await act.Should().ThrowAsync<KeyNotFoundException>();
    }
}
