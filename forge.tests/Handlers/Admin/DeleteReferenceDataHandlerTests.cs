using Bogus;
using FluentAssertions;
using Forge.Api.Features.Admin;
using Forge.Core.Entities;
using Forge.Tests.Helpers;

namespace Forge.Tests.Handlers.Admin;

public class DeleteReferenceDataHandlerTests
{
    private readonly DeleteReferenceDataHandler _handler;
    private readonly Data.Context.AppDbContext _db;
    private readonly Faker _faker = new();

    public DeleteReferenceDataHandlerTests()
    {
        _db = TestDbContextFactory.Create();
        _handler = new DeleteReferenceDataHandler(_db);
    }

    [Fact]
    public async Task Handle_UserCreatedEntry_DeletesSuccessfully()
    {
        // Arrange
        var entry = new ReferenceData
        {
            GroupCode = "test_group",
            Code = _faker.Random.AlphaNumeric(8),
            Label = _faker.Commerce.ProductName(),
            SortOrder = 1,
            IsSeedData = false,
        };
        _db.ReferenceData.Add(entry);
        await _db.SaveChangesAsync();

        var command = new DeleteReferenceDataCommand(entry.Id);

        // Act
        await _handler.Handle(command, CancellationToken.None);

        // Assert
        var remaining = _db.ReferenceData.FirstOrDefault(r => r.Id == entry.Id);
        remaining.Should().BeNull();
    }

    [Fact]
    public async Task Handle_SeedDataEntry_ThrowsInvalidOperationException()
    {
        // Arrange
        var entry = new ReferenceData
        {
            GroupCode = "job_priority",
            Code = "normal",
            Label = "Normal",
            SortOrder = 2,
            IsSeedData = true,
        };
        _db.ReferenceData.Add(entry);
        await _db.SaveChangesAsync();

        var command = new DeleteReferenceDataCommand(entry.Id);

        // Act
        var act = () => _handler.Handle(command, CancellationToken.None);

        // Assert
        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*Seed data*cannot be deleted*");
    }

    [Fact]
    public async Task Handle_NonExistentId_ThrowsKeyNotFoundException()
    {
        var command = new DeleteReferenceDataCommand(99999);

        var act = () => _handler.Handle(command, CancellationToken.None);

        await act.Should().ThrowAsync<KeyNotFoundException>();
    }
}
