using Bogus;
using FluentAssertions;
using Forge.Api.Features.Training;
using Forge.Tests.Helpers;

namespace Forge.Tests.Handlers.Training;

public class CreateTrainingModuleHandlerTests
{
    private readonly CreateTrainingModuleHandler _handler;
    private readonly Data.Context.AppDbContext _db;
    private readonly Faker _faker = new();

    public CreateTrainingModuleHandlerTests()
    {
        _db = TestDbContextFactory.Create();
        _handler = new CreateTrainingModuleHandler(_db);
    }

    [Fact]
    public async Task Handle_ValidCommand_CreatesModuleAndReturnsDetail()
    {
        var title = _faker.Commerce.ProductName();
        var slug = _faker.Internet.DomainWord();
        var command = new CreateTrainingModuleCommand(
            title, slug, "Module summary", TrainingContentType.Article,
            "{}", null, 15, new[] { "safety" }, new[] { "/dashboard" },
            true, false, 1, 1);

        var result = await _handler.Handle(command, CancellationToken.None);

        result.Should().NotBeNull();
        result.Title.Should().Be(title);
        result.Slug.Should().Be(slug);
        result.IsPublished.Should().BeTrue();
        result.EstimatedMinutes.Should().Be(15);
    }

    [Fact]
    public async Task Handle_SetsAllFieldsCorrectly()
    {
        var command = new CreateTrainingModuleCommand(
            "Safety Basics", "safety-basics", "Safety overview", TrainingContentType.Article,
            "{\"blocks\":[]}", "https://example.com/cover.jpg", 30,
            new[] { "safety", "required" }, new[] { "/dashboard" },
            false, true, 5, 1);

        var result = await _handler.Handle(command, CancellationToken.None);

        result.Slug.Should().Be("safety-basics");
        result.Summary.Should().Be("Safety overview");
        result.EstimatedMinutes.Should().Be(30);
        result.IsPublished.Should().BeFalse();
        result.IsOnboardingRequired.Should().BeTrue();
        result.SortOrder.Should().Be(5);
        result.Tags.Should().Contain("safety");
    }
}
