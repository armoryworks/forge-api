using FluentAssertions;

using Forge.Core.Entities;
using Forge.Core.Enums;
using Forge.Data.Repositories;
using Forge.Tests.Helpers;

namespace Forge.Tests.Repositories;

public class QuoteRepositoryListTotalTests
{
    [Fact]
    public async Task GetAllAsync_TotalIsTheRoundedLinesPlusTheirTaxRoundedToTheCent()
    {
        using var db = TestDbContextFactory.Create();
        var customer = new Customer { Name = "Acme" };
        db.Customers.Add(customer);
        await db.SaveChangesAsync();

        var quote = new Quote { QuoteNumber = "Q-1", CustomerId = customer.Id, Status = QuoteStatus.Draft, TaxRate = 0.0725m };
        quote.Lines.Add(new QuoteLine { Description = "Eighths", Quantity = 3m, UnitPrice = 0.125m, LineNumber = 1 });
        quote.Lines.Add(new QuoteLine { Description = "Half", Quantity = 0.5m, UnitPrice = 19.999m, LineNumber = 2 });
        quote.Lines.Add(new QuoteLine { Description = "Whole", Quantity = 1m, UnitPrice = 10m, LineNumber = 3 });
        db.Quotes.Add(quote);
        await db.SaveChangesAsync();

        var list = await new QuoteRepository(db).GetAllAsync(null, null, CancellationToken.None);

        list.Should().ContainSingle().Which.Total.Should().Be(21.86m);
    }
}
