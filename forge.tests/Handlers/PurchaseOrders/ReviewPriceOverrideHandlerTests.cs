using FluentAssertions;
using MediatR;
using Moq;

using Forge.Api.Features.PurchaseOrders;
using Forge.Api.Features.VendorParts;
using Forge.Core.Interfaces;
using Forge.Core.Models;

namespace Forge.Tests.Handlers.PurchaseOrders;

public class ReviewPriceOverrideHandlerTests
{
    private readonly Mock<IMediator> _mediator = new();
    private readonly Mock<IAiService> _ai = new();
    private readonly ReviewPriceOverrideHandler _handler;

    public ReviewPriceOverrideHandlerTests()
    {
        _handler = new ReviewPriceOverrideHandler(_mediator.Object, _ai.Object);
    }

    private void SetupVariance(decimal? tierPrice, decimal? variancePct, bool isOffTier)
    {
        _mediator.Setup(m => m.Send(It.IsAny<CheckTierVarianceQuery>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new CheckTierVarianceResponseModel(5m, new List<CheckTierVarianceResultModel>
            {
                new(PartId: 3, Quantity: 10, UnitPrice: 9m, VendorPartId: 1,
                    TierPrice: tierPrice, Currency: "USD", VariancePct: variancePct, IsOffTier: isOffTier),
            }));
    }

    private static ReviewPriceOverrideQuery Query(decimal entered = 9m)
        => new(new ReviewPriceOverrideRequestModel(VendorId: 1, PartId: 3, Quantity: 10,
            PurchaseUnitId: null, EnteredUnitPrice: entered, Reason: "rush buy"));

    [Fact]
    public async Task WithinTier_isLowRisk_andUsesDeterministicAssessmentWhenAiOffline()
    {
        SetupVariance(tierPrice: 9m, variancePct: 0m, isOffTier: false);
        _ai.Setup(a => a.IsAvailableAsync(It.IsAny<CancellationToken>())).ReturnsAsync(false);

        var result = await _handler.Handle(Query(), CancellationToken.None);

        result.RiskLevel.Should().Be("Low");
        result.IsOffTier.Should().BeFalse();
        result.AiAvailable.Should().BeFalse();
        result.Assessment.Should().NotBeNullOrWhiteSpace();
        result.SuggestedJustification.Should().Contain("rush buy");
        _ai.Verify(a => a.GenerateTextAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<double?>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task LargeVariance_isHighRisk()
    {
        SetupVariance(tierPrice: 4m, variancePct: 60m, isOffTier: true);
        _ai.Setup(a => a.IsAvailableAsync(It.IsAny<CancellationToken>())).ReturnsAsync(false);

        var result = await _handler.Handle(Query(entered: 6.4m), CancellationToken.None);

        result.RiskLevel.Should().Be("High");
        result.IsOffTier.Should().BeTrue();
    }

    [Fact]
    public async Task ModerateVariance_isMediumRisk_andUsesAiNarrativeWhenAvailable()
    {
        SetupVariance(tierPrice: 9m, variancePct: 12m, isOffTier: true);
        _ai.Setup(a => a.IsAvailableAsync(It.IsAny<CancellationToken>())).ReturnsAsync(true);
        _ai.Setup(a => a.GenerateTextAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<double?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync("Looks acceptable given the rush; confirm vendor quote on file.");

        var result = await _handler.Handle(Query(), CancellationToken.None);

        result.RiskLevel.Should().Be("Medium");
        result.AiAvailable.Should().BeTrue();
        result.Assessment.Should().Be("Looks acceptable given the rush; confirm vendor quote on file.");
    }
}
