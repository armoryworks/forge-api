using FluentAssertions;
using Moq;

using Forge.Api.Features.Quotes;
using Forge.Core.Entities;
using Forge.Core.Interfaces;

namespace Forge.Tests.Handlers.Quotes;

public class GetQuoteByIdRowVersionTests
{
    [Fact]
    public async Task Detail_carries_the_concurrency_version_for_the_next_If_Match()
    {
        var quote = new Quote
        {
            Id = 7, QuoteNumber = "Q-7", CustomerId = 1, Version = 5,
            Customer = new Customer { Id = 1, Name = "Acme" },
            Lines = [new QuoteLine { Id = 1, LineNumber = 1, Description = "Line", Quantity = 1m, UnitPrice = 10m }],
        };
        var repo = new Mock<IQuoteRepository>();
        repo.Setup(r => r.FindWithDetailsAsync(7, It.IsAny<CancellationToken>())).ReturnsAsync(quote);

        var detail = await new GetQuoteByIdHandler(repo.Object, null)
            .Handle(new GetQuoteByIdQuery(7), CancellationToken.None);

        detail.RowVersion.Should().Be(5u);
    }
}
