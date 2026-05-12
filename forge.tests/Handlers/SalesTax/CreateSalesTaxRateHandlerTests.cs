using FluentAssertions;

using Forge.Api.Features.SalesTax;
using Forge.Core.Entities;
using Forge.Core.Models;
using Forge.Tests.Helpers;

namespace Forge.Tests.Handlers.SalesTax;

public class CreateSalesTaxRateHandlerTests
{
    [Fact]
    public async Task Handle_ValidCommand_CreatesTaxRate()
    {
        // Arrange
        using var db = TestDbContextFactory.Create();
        var handler = new CreateSalesTaxRateHandler(db);

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

        var handler = new CreateSalesTaxRateHandler(db);
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
        var handler = new CreateSalesTaxRateHandler(db);

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
}
