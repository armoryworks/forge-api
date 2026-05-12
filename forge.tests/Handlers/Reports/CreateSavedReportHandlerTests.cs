using System.Security.Claims;

using FluentAssertions;
using Moq;

using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;

using Forge.Api.Features.Reports;
using Forge.Core.Entities;
using Forge.Core.Enums;
using Forge.Core.Interfaces;
using Forge.Core.Models;
using Forge.Data.Context;

namespace Forge.Tests.Handlers.Reports;

public class CreateSavedReportHandlerTests
{
    private readonly Mock<IReportBuilderRepository> _repo = new();
    private readonly Mock<UserManager<ApplicationUser>> _userManager;
    private readonly Mock<IHttpContextAccessor> _httpContext = new();

    public CreateSavedReportHandlerTests()
    {
        var store = new Mock<IUserStore<ApplicationUser>>();
        _userManager = new Mock<UserManager<ApplicationUser>>(
            store.Object, null!, null!, null!, null!, null!, null!, null!, null!);
    }

    private void SetupAuthenticatedUser(int userId)
    {
        var claims = new ClaimsPrincipal(new ClaimsIdentity(
        [
            new Claim(ClaimTypes.NameIdentifier, userId.ToString()),
        ], "test"));
        var httpContext = new DefaultHttpContext { User = claims };
        _httpContext.Setup(x => x.HttpContext).Returns(httpContext);
    }

    [Fact]
    public async Task Handle_ValidCommand_CreatesSavedReport()
    {
        // Arrange
        SetupAuthenticatedUser(1);

        var command = new CreateSavedReportCommand(
            "Monthly Jobs", "Jobs report", "Jobs",
            ["Title", "JobNumber", "Status"],
            null, null, "CreatedAt", "desc", "bar", "Status", "Id", false);

        var savedReport = new SavedReport
        {
            Id = 10,
            Name = "Monthly Jobs",
            Description = "Jobs report",
            EntitySource = "Jobs",
            ColumnsJson = "[\"Title\",\"JobNumber\",\"Status\"]",
            SortField = "CreatedAt",
            SortDirection = "desc",
            ChartType = "bar",
            UserId = 1,
        };

        _repo.Setup(r => r.Create(It.IsAny<SavedReport>())).ReturnsAsync(savedReport);
        _repo.Setup(r => r.GetById(It.IsAny<int>())).ReturnsAsync(savedReport);

        _userManager.Setup(u => u.FindByIdAsync("1"))
            .ReturnsAsync(new ApplicationUser { Id = 1, UserName = "admin", FirstName = "Admin", LastName = "User" });

        var handler = new CreateSavedReportHandler(_repo.Object, _userManager.Object, _httpContext.Object);

        // Act
        var result = await handler.Handle(command, CancellationToken.None);

        // Assert
        result.Should().NotBeNull();
        result.Name.Should().Be("Monthly Jobs");

        _repo.Verify(r => r.Create(It.Is<SavedReport>(s =>
            s.Name == "Monthly Jobs" &&
            s.EntitySource == "Jobs" &&
            s.UserId == 1 &&
            s.ChartType == "bar"
        )), Times.Once);
    }

    [Fact]
    public async Task Handle_NoAuthenticatedUser_ThrowsUnauthorized()
    {
        // Arrange
        _httpContext.Setup(x => x.HttpContext).Returns((HttpContext?)null);

        var handler = new CreateSavedReportHandler(_repo.Object, _userManager.Object, _httpContext.Object);
        var command = new CreateSavedReportCommand(
            "Test", null, "Jobs", ["Title"], null, null, null, null, null, null, null, false);

        // Act
        var act = () => handler.Handle(command, CancellationToken.None);

        // Assert
        await act.Should().ThrowAsync<UnauthorizedAccessException>();
    }

    [Fact]
    public async Task Handle_SerializesColumnsAndFilters()
    {
        // Arrange
        SetupAuthenticatedUser(5);

        var filters = new[]
        {
            new ReportFilterModel("Status", ReportFilterOperator.Equals, "Active"),
        };

        var command = new CreateSavedReportCommand(
            "Filtered Report", null, "Parts",
            ["PartNumber", "Description"],
            filters, "Status", null, null, null, null, null, true);

        SavedReport? capturedReport = null;
        _repo.Setup(r => r.Create(It.IsAny<SavedReport>()))
            .Callback<SavedReport>(r => capturedReport = r)
            .ReturnsAsync((SavedReport r) => r);
        _repo.Setup(r => r.GetById(It.IsAny<int>()))
            .Returns(() => Task.FromResult(capturedReport));

        _userManager.Setup(u => u.FindByIdAsync("5"))
            .ReturnsAsync(new ApplicationUser { Id = 5, UserName = "user5", FirstName = "Test", LastName = "User" });

        var handler = new CreateSavedReportHandler(_repo.Object, _userManager.Object, _httpContext.Object);

        // Act
        var result = await handler.Handle(command, CancellationToken.None);

        // Assert
        capturedReport.Should().NotBeNull();
        capturedReport!.ColumnsJson.Should().Contain("PartNumber");
        capturedReport.FiltersJson.Should().NotBeNullOrEmpty();
        capturedReport.IsShared.Should().BeTrue();
        capturedReport.GroupByField.Should().Be("Status");
    }
}
