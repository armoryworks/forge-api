using FluentAssertions;

using Forge.Api.Features.SalesTax;
using Forge.Core.Entities;
using Forge.Core.Models;
using Forge.Integrations;
using Forge.Tests.Helpers;

namespace Forge.Tests.Handlers.SalesTax;

public class CreateSalesTaxRateHandlerTests
{
    [Fact]
    public async Task Handle_ValidCommand_CreatesTaxRate()
    {
        // Arrange
        using var db = TestDbContextFactory.Create();
        var handler = new CreateSalesTaxRateHandler(db, new SystemClock());

        // Use null stateCode to avoid ExecuteUpdateAsync (not supported by InMemory provider)
        var data = new CreateSalesTaxRateRequestModel(
            "Default Sales Tax", "DEFAULT-TAX", null, 0.0725m, null, false, "Default tax rate");
        var command = new CreateSalesTaxRateCommand(data);

        // Act
        var result = await handler.Handle(command, CancellationToken.None);

        // Assert
        result.Should().NotBeNull();
        result.Name.Should().Be("Default Sales Tax");
        result.Code.Should().Be("DEFAULT-TAX");
        result.StateCode.Should().BeNull();
        result.Rate.Should().Be(0.0725m);
        result.IsDefault.Should().BeFalse();
        result.Description.Should().Be("Default tax rate");

        db.SalesTaxRates.Should().HaveCount(1);
    }

    [Fact]
    public async Task Handle_DuplicateCode_ThrowsInvalidOperationException()
    {
        // Arrange
        using var db = TestDbContextFactory.Create();
        db.SalesTaxRates.Add(new SalesTaxRate
        {
            Name = "Existing",
            Code = "OH-SALES",
            Rate = 0.07m,
            EffectiveFrom = DateTimeOffset.UtcNow,
        });
        await db.SaveChangesAsync();

        var handler = new CreateSalesTaxRateHandler(db, new SystemClock());
        var data = new CreateSalesTaxRateRequestModel(
            "Duplicate", "OH-SALES", null, 0.08m, null, false, null);
        var command = new CreateSalesTaxRateCommand(data);

        // Act
        var act = () => handler.Handle(command, CancellationToken.None);

        // Assert
        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*OH-SALES*already exists*");
    }

    [Fact]
    public async Task Handle_WhitespaceStateCode_TreatedAsNull()
    {
        // Arrange
        using var db = TestDbContextFactory.Create();
        var handler = new CreateSalesTaxRateHandler(db, new SystemClock());

        // Empty/whitespace stateCode should be normalized to null
        var data = new CreateSalesTaxRateRequestModel(
            "No State Tax", "NO-STATE", "  ", 0.05m, null, false, null);
        var command = new CreateSalesTaxRateCommand(data);

        // Act
        var result = await handler.Handle(command, CancellationToken.None);

        // Assert
        result.StateCode.Should().BeNull();
        result.Code.Should().Be("NO-STATE");

        var saved = db.SalesTaxRates.Single();
        saved.StateCode.Should().BeNull();
    }

    private static readonly DateTimeOffset Jan1 = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

    private static CreateSalesTaxRateCommand Utah(string code, decimal rate, DateTimeOffset from, bool exempt = false) =>
        new(new CreateSalesTaxRateRequestModel(code, code, "UT", rate, from, false, null, exempt));

    [Fact]
    public async Task Handle_SameStateSameStartDate_KeepsBothJurisdictionRatesOpen()
    {
        using var db = TestDbContextFactory.Create();
        var handler = new CreateSalesTaxRateHandler(db, new SystemClock());

        await handler.Handle(Utah("UT-STATE", 0.0485m, Jan1), CancellationToken.None);
        await handler.Handle(Utah("UT-UTCO", 0.0725m, Jan1), CancellationToken.None);

        db.SalesTaxRates.Where(r => r.EffectiveTo == null).Select(r => r.Code)
            .Should().BeEquivalentTo(["UT-STATE", "UT-UTCO"]);
    }

    [Fact]
    public async Task Handle_LaterStartDate_SupersedesTheEarlierRate()
    {
        using var db = TestDbContextFactory.Create();
        var handler = new CreateSalesTaxRateHandler(db, new SystemClock());

        await handler.Handle(Utah("UT-2025", 0.061m, Jan1.AddYears(-1)), CancellationToken.None);
        await handler.Handle(Utah("UT-2026", 0.0725m, Jan1), CancellationToken.None);

        db.SalesTaxRates.Single(r => r.Code == "UT-2025").EffectiveTo.Should().Be(Jan1);
        db.SalesTaxRates.Single(r => r.Code == "UT-2026").EffectiveTo.Should().BeNull();
    }

    [Fact]
    public async Task Handle_ExemptionMarker_DoesNotEndTheTaxableRate()
    {
        using var db = TestDbContextFactory.Create();
        var handler = new CreateSalesTaxRateHandler(db, new SystemClock());

        await handler.Handle(Utah("UT-UTCO", 0.0725m, Jan1.AddDays(-30)), CancellationToken.None);
        await handler.Handle(Utah("EXEMPT-RESALE", 0m, Jan1, exempt: true), CancellationToken.None);

        db.SalesTaxRates.Where(r => r.EffectiveTo == null).Select(r => r.Code)
            .Should().BeEquivalentTo(["UT-UTCO", "EXEMPT-RESALE"]);
    }
}
