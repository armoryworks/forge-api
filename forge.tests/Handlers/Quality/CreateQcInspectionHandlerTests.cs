using System.Security.Claims;
using Bogus;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Moq;
using Forge.Api.Features.Quality;
using Forge.Core.Entities;
using Forge.Core.Models;
using Forge.Tests.Helpers;

namespace Forge.Tests.Handlers.Quality;

public class CreateQcInspectionHandlerTests
{
    private readonly CreateQcInspectionHandler _handler;
    private readonly Data.Context.AppDbContext _db;
    private readonly Faker _faker = new();
    private readonly int _userId;

    public CreateQcInspectionHandlerTests()
    {
        _userId = _faker.Random.Int(1, 50);
        _db = TestDbContextFactory.Create();

        var httpContextAccessor = new Mock<IHttpContextAccessor>();
        var claims = new List<Claim> { new(ClaimTypes.NameIdentifier, _userId.ToString()) };
        var identity = new ClaimsIdentity(claims, "Test");
        var httpContext = new DefaultHttpContext { User = new ClaimsPrincipal(identity) };
        httpContextAccessor.Setup(a => a.HttpContext).Returns(httpContext);

        _handler = new CreateQcInspectionHandler(_db, httpContextAccessor.Object);
    }

    [Fact]
    public async Task Handle_ValidData_CreatesInspection()
    {
        // Arrange — create a template with items
        var template = new QcChecklistTemplate
        {
            Name = "Visual Check",
            IsActive = true,
        };
        _db.QcChecklistTemplates.Add(template);
        await _db.SaveChangesAsync();

        var data = new CreateQcInspectionRequestModel(
            null, null, template.Id, null, "Looks good");

        var command = new CreateQcInspectionCommand(data);

        // Act
        var result = await _handler.Handle(command, CancellationToken.None);

        // Assert
        result.Should().NotBeNull();
        result.TemplateId.Should().Be(template.Id);
        result.Status.Should().Be("InProgress");
    }

    [Fact]
    public async Task Handle_WithoutTemplate_CreatesInspection()
    {
        var data = new CreateQcInspectionRequestModel(
            null, null, null, "LOT-001", "Manual inspection");

        var command = new CreateQcInspectionCommand(data);

        var result = await _handler.Handle(command, CancellationToken.None);

        result.Should().NotBeNull();
        result.LotNumber.Should().Be("LOT-001");
        result.Status.Should().Be("InProgress");
    }
}
