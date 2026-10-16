using FluentAssertions;
using Moq;

using Forge.Api.Features.Quotes;
using Forge.Core.Entities;
using Forge.Core.Interfaces;
using Forge.Core.Models;
using Forge.Core.Models.Accounting;

namespace Forge.Tests.Handlers.Quotes;

public class GetQuoteByIdMarginTests
{
    private const int CostedPartId = 10;
    private const int UncostedPartId = 11;

    private static Quote QuoteWithLines() => new()
    {
        Id = 1,
        QuoteNumber = "Q-1",
        CustomerId = 1,
        Customer = new Customer { Id = 1, Name = "Acme" },
        Lines =
        [
            new QuoteLine { Id = 1, LineNumber = 1, PartId = CostedPartId, Description = "Costed", Quantity = 2m, UnitPrice = 50m },
            new QuoteLine { Id = 2, LineNumber = 2, PartId = UncostedPartId, Description = "No standard", Quantity = 1m, UnitPrice = 20m },
            new QuoteLine { Id = 3, LineNumber = 3, PartId = null, Description = "Freeform", Quantity = 1m, UnitPrice = 5m },
            new QuoteLine { Id = 4, LineNumber = 4, PartId = CostedPartId, Description = "No charge", Quantity = 1m, UnitPrice = 0m },
        ],
    };

    private static async Task<QuoteDetailResponseModel> GetAsync(IStandardCostResolver? costs)
    {
        var repo = new Mock<IQuoteRepository>();
        repo.Setup(r => r.FindWithDetailsAsync(1, It.IsAny<CancellationToken>())).ReturnsAsync(QuoteWithLines());
        return await new GetQuoteByIdHandler(repo.Object, costs).Handle(new GetQuoteByIdQuery(1), CancellationToken.None);
    }

    [Fact]
    public async Task LineWithAStandardCost_CarriesItsBreakdownAndEffectiveMargin()
    {
        var costs = new Mock<IStandardCostResolver>();
        costs.Setup(c => c.ResolveAsync(CostedPartId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new StandardCostElements(Material: 20m, Labor: 8m, Overhead: 2m));
        costs.Setup(c => c.ResolveAsync(UncostedPartId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(StandardCostElements.Zero);

        var quote = await GetAsync(costs.Object);

        var costed = quote.Lines.Single(l => l.Id == 1);
        costed.CostBreakdown.Should().Be(new QuoteLineCostBreakdownModel(Labor: 8m, Burden: 2m, Material: 20m, Osp: 0m, Total: 30m));
        costed.EffectiveMargin.Should().Be(0.4m);
        quote.Lines.Single(l => l.Id == 2).Should().Match<QuoteLineResponseModel>(l => l.CostBreakdown == null && l.EffectiveMargin == null);
        quote.Lines.Single(l => l.Id == 3).Should().Match<QuoteLineResponseModel>(l => l.CostBreakdown == null && l.EffectiveMargin == null);
        quote.Lines.Single(l => l.Id == 4).EffectiveMargin.Should().BeNull();
        costs.Verify(c => c.ResolveAsync(CostedPartId, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task WithoutACostResolver_LinesCarryNoMargin()
    {
        var quote = await GetAsync(costs: null);

        quote.Lines.Should().OnlyContain(l => l.CostBreakdown == null && l.EffectiveMargin == null);
    }
}
