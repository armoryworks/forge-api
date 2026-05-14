using FluentAssertions;
using Moq;
using Forge.Api.Features.Inventory;
using Forge.Core.Interfaces;
using Forge.Core.Models;

namespace Forge.Tests.Handlers.Inventory;

/// <summary>
/// Verifies the bin-locations handler forwards search + pagination to the
/// repository and returns the paged envelope expected by
/// <c>&lt;app-entity-picker&gt;</c>.
/// </summary>
public class GetBinLocationsHandlerTests
{
    private readonly Mock<IInventoryRepository> _repo = new();
    private readonly GetBinLocationsHandler _handler;

    public GetBinLocationsHandlerTests()
    {
        _handler = new GetBinLocationsHandler(_repo.Object);
    }

    [Fact]
    public async Task Handle_PassesSearchAndPagingThrough()
    {
        var expected = new PagedResponse<StorageLocationFlatResponseModel>(
            new List<StorageLocationFlatResponseModel>
            {
                new(1, "BIN-A1", LocationType.Bin, "BC-1", "Warehouse / BIN-A1"),
            },
            TotalCount: 1, Page: 2, PageSize: 50);

        _repo.Setup(r => r.GetBinLocationsPagedAsync("a1", 2, 50, It.IsAny<CancellationToken>()))
            .ReturnsAsync(expected);

        var result = await _handler.Handle(
            new GetBinLocationsQuery("a1", 2, 50), CancellationToken.None);

        result.Should().BeSameAs(expected);
        _repo.Verify(r => r.GetBinLocationsPagedAsync("a1", 2, 50, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Handle_DefaultsApplyWhenNoArgsProvided()
    {
        _repo.Setup(r => r.GetBinLocationsPagedAsync(null, 1, 20, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PagedResponse<StorageLocationFlatResponseModel>(
                new List<StorageLocationFlatResponseModel>(), 0, 1, 20));

        var result = await _handler.Handle(new GetBinLocationsQuery(), CancellationToken.None);

        result.Page.Should().Be(1);
        result.PageSize.Should().Be(20);
        _repo.Verify(r => r.GetBinLocationsPagedAsync(null, 1, 20, It.IsAny<CancellationToken>()), Times.Once);
    }
}
