using Bogus;
using FluentAssertions;

using Forge.Api.Features.ComplianceForms;
using Forge.Core.Enums;
using Forge.Core.Models;
using Forge.Tests.Helpers;

namespace Forge.Tests.Handlers.ComplianceForms;

public class CreateComplianceTemplateHandlerTests
{
    private readonly Faker _faker = new();

    [Fact]
    public async Task Handle_ValidCommand_CreatesTemplateAndReturnsResponse()
    {
        // Arrange
        using var db = TestDbContextFactory.Create();
        var handler = new CreateComplianceTemplateHandler(db);

        var model = new CreateComplianceFormTemplateRequestModel(
            _faker.Commerce.ProductName(),
            ComplianceFormType.W4,
            "Federal W-4 form",
            "description_icon",
            "https://irs.gov/w4.pdf",
            true, true, 1, false, true, "w4");

        var command = new CreateComplianceTemplateCommand(model);

        // Act
        var result = await handler.Handle(command, CancellationToken.None);

        // Assert
        result.Should().NotBeNull();
        result.Name.Should().Be(model.Name);
        result.FormType.Should().Be(ComplianceFormType.W4);
        result.IsActive.Should().BeTrue();
        result.SortOrder.Should().Be(1);
        result.ProfileCompletionKey.Should().Be("w4");

        db.ComplianceFormTemplates.Should().HaveCount(1);
    }

    [Fact]
    public async Task Handle_SetsAllFieldsCorrectly()
    {
        // Arrange
        using var db = TestDbContextFactory.Create();
        var handler = new CreateComplianceTemplateHandler(db);

        var model = new CreateComplianceFormTemplateRequestModel(
            "I-9 Employment Eligibility",
            ComplianceFormType.I9,
            "I-9 verification form",
            "badge_icon",
            null,
            false, false, 5, true, false, "i9");

        var command = new CreateComplianceTemplateCommand(model);

        // Act
        var result = await handler.Handle(command, CancellationToken.None);

        // Assert
        result.RequiresIdentityDocs.Should().BeTrue();
        result.BlocksJobAssignment.Should().BeFalse();
        result.IsAutoSync.Should().BeFalse();
        result.IsActive.Should().BeFalse();
        result.SourceUrl.Should().BeNull();
        result.Description.Should().Be("I-9 verification form");
    }

    [Fact]
    public async Task Handle_PersistsToDatabase()
    {
        // Arrange
        using var db = TestDbContextFactory.Create();
        var handler = new CreateComplianceTemplateHandler(db);

        var model = new CreateComplianceFormTemplateRequestModel(
            "State Withholding",
            ComplianceFormType.StateWithholding,
            "State tax form",
            "location_icon",
            "https://example.com/state.pdf",
            true, true, 3, false, true, "state_wh");

        var command = new CreateComplianceTemplateCommand(model);

        // Act
        await handler.Handle(command, CancellationToken.None);

        // Assert
        var saved = db.ComplianceFormTemplates.Single();
        saved.Name.Should().Be("State Withholding");
        saved.FormType.Should().Be(ComplianceFormType.StateWithholding);
        saved.SourceUrl.Should().Be("https://example.com/state.pdf");
    }
}
