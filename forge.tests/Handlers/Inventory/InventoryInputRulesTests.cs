using System.Security.Claims;

using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Moq;

using Forge.Api.Features.Inventory;
using Forge.Core.Entities;
using Forge.Core.Enums;
using Forge.Core.Interfaces;
using Forge.Core.Models;

namespace Forge.Tests.Handlers.Inventory;

public class InventoryInputRulesTests
{
    [Fact]
    public void CycleCount_RefusesANegativeCountedQuantity()
    {
        var validator = new UpdateCycleCountCommandValidator();

        var result = validator.Validate(new UpdateCycleCountCommand(1, new UpdateCycleCountRequestModel(
            null, null, [new UpdateCycleCountLineModel(1, -3, null), new UpdateCycleCountLineModel(2, 0, null)])));

        result.Errors.Should().ContainSingle()
            .Which.ErrorMessage.Should().Be("A counted quantity cannot be negative.");
    }

    [Fact]
    public void CycleCount_WithoutLines_IsValid()
    {
        new UpdateCycleCountCommandValidator()
            .Validate(new UpdateCycleCountCommand(1, new UpdateCycleCountRequestModel("Completed", null, null)))
            .IsValid.Should().BeTrue();
    }

    [Fact]
    public void StorageLocation_NamesTheFieldPlainlyInItsErrors()
    {
        var create = new CreateStorageLocationCommandValidator().Validate(new CreateStorageLocationCommand(
            new CreateStorageLocationRequestModel("Bin", LocationType.Bin, null, null, new string('x', 501))));
        var update = new UpdateStorageLocationCommandValidator().Validate(new UpdateStorageLocationCommand(
            1, new UpdateStorageLocationRequestModel("Bin", LocationType.Bin, null, null, new string('x', 501))));

        create.Errors.Should().ContainSingle().Which.ErrorMessage.Should().Contain("'Description'").And.NotContain("Data");
        update.Errors.Should().ContainSingle().Which.ErrorMessage.Should().Contain("'Description'").And.NotContain("Data");
    }

    [Fact]
    public async Task Transfer_ShowsQuantitiesWithoutTrailingZeros()
    {
        var repo = new Mock<IInventoryRepository>();
        repo.Setup(r => r.FindBinContentWithLocationAsync(5, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new BinContent { Id = 5, LocationId = 1, EntityId = 1, Quantity = 4m });
        var http = new Mock<IHttpContextAccessor>();
        http.Setup(h => h.HttpContext).Returns(new DefaultHttpContext
        {
            User = new ClaimsPrincipal(new ClaimsIdentity([new Claim(ClaimTypes.NameIdentifier, "1")])),
        });
        var handler = new TransferStockHandler(repo.Object, http.Object, new Mock<IClock>().Object);

        var act = () => handler.Handle(
            new TransferStockCommand(new TransferStockRequestModel(5, 2, 6, null)), CancellationToken.None);

        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("Cannot transfer 6 — only 4 available");
    }
}
