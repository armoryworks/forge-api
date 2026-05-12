using Bogus;
using FluentAssertions;
using Forge.Api.Features.Quality;
using Forge.Core.Models;
using Forge.Tests.Helpers;

namespace Forge.Tests.Handlers.Quality;

public class CreateQcTemplateHandlerTests
{
    private readonly CreateQcTemplateHandler _handler;
    private readonly Data.Context.AppDbContext _db;
    private readonly Faker _faker = new();

    public CreateQcTemplateHandlerTests()
    {
        _db = TestDbContextFactory.Create();
        _handler = new CreateQcTemplateHandler(_db);
    }

    [Fact]
    public async Task Handle_ValidData_CreatesTemplateAndReturnsModel()
    {
        var name = _faker.Commerce.ProductName();
        var items = new List<CreateQcTemplateItemModel>
        {
            new("Check dimension A", "10mm ± 0.5", 1, true),
        };
        var data = new CreateQcTemplateRequestModel(name, "Test template description", null, items);

        var command = new CreateQcTemplateCommand(data);

        var result = await _handler.Handle(command, CancellationToken.None);

        result.Should().NotBeNull();
        result.Name.Should().Be(name);
        result.IsActive.Should().BeTrue();
        result.Items.Should().HaveCount(1);
    }

    [Fact]
    public async Task Handle_PersistsToDatabase()
    {
        var items = new List<CreateQcTemplateItemModel>
        {
            new("Check length", "100mm", 1, true),
            new("Check width", "50mm", 2, false),
        };
        var data = new CreateQcTemplateRequestModel(
            "Dimensional Check", "Check all dimensions", null, items);

        var command = new CreateQcTemplateCommand(data);

        var result = await _handler.Handle(command, CancellationToken.None);

        var stored = _db.QcChecklistTemplates.First(t => t.Id == result.Id);
        stored.Name.Should().Be("Dimensional Check");
        stored.Items.Should().HaveCount(2);
    }
}
