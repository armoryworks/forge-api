using FluentAssertions;
using Moq;

using Microsoft.AspNetCore.Identity;

using Forge.Api.Data;
using Forge.Api.Features.Employees;
using Forge.Core.Entities;
using Forge.Core.Models;
using Forge.Data.Context;
using Forge.Tests.Helpers;

namespace Forge.Tests.Handlers.Employees;

public class GetEmployeeListHandlerTests
{
    private readonly Mock<UserManager<ApplicationUser>> _userManager;

    public GetEmployeeListHandlerTests()
    {
        var store = new Mock<IUserStore<ApplicationUser>>();
        _userManager = new Mock<UserManager<ApplicationUser>>(
            store.Object, null!, null!, null!, null!, null!, null!, null!, null!);

        // The handler excludes service identities (LeadIntake role) from the
        // roster. Default to no service users so existing assertions hold;
        // a dedicated test overrides this to verify the exclusion.
        _userManager.Setup(u => u.GetUsersInRoleAsync(It.IsAny<string>()))
            .ReturnsAsync([]);
    }

    [Fact]
    public async Task Handle_ReturnsAllActiveUsers()
    {
        // Arrange
        using var db = TestDbContextFactory.Create();
        db.Users.AddRange(
            new ApplicationUser { Id = 1, FirstName = "Dan", LastName = "Hokanson", Initials = "DH", AvatarColor = "#4f46e5", Email = "dan@test.com", UserName = "dan", IsActive = true },
            new ApplicationUser { Id = 2, FirstName = "Jane", LastName = "Doe", Initials = "JD", AvatarColor = "#ef4444", Email = "jane@test.com", UserName = "jane", IsActive = true },
            new ApplicationUser { Id = 3, FirstName = "Inactive", LastName = "User", Initials = "IU", AvatarColor = "#94a3b8", Email = "inactive@test.com", UserName = "inactive", IsActive = false });
        await db.SaveChangesAsync();

        _userManager.Setup(u => u.GetRolesAsync(It.IsAny<ApplicationUser>()))
            .ReturnsAsync(["Engineer"]);

        var handler = new GetEmployeeListHandler(db, _userManager.Object);
        var query = new GetEmployeeListQuery(
            new EmployeeListQuery { IsActive = true }, null, true);

        // Act
        var result = await handler.Handle(query, CancellationToken.None);

        // Assert (Phase 3 F7-broad / WU-22 — paged envelope)
        result.Items.Should().HaveCount(2);
        result.Items.All(e => e.IsActive).Should().BeTrue();
    }

    [Fact]
    public async Task Handle_SearchByName_FiltersCorrectly()
    {
        // Arrange
        using var db = TestDbContextFactory.Create();
        db.Users.AddRange(
            new ApplicationUser { Id = 1, FirstName = "Dan", LastName = "Hokanson", Initials = "DH", AvatarColor = "#4f46e5", Email = "dan@test.com", UserName = "dan", IsActive = true },
            new ApplicationUser { Id = 2, FirstName = "Jane", LastName = "Smith", Initials = "JS", AvatarColor = "#ef4444", Email = "jane@test.com", UserName = "jane", IsActive = true });
        await db.SaveChangesAsync();

        _userManager.Setup(u => u.GetRolesAsync(It.IsAny<ApplicationUser>()))
            .ReturnsAsync(["Engineer"]);

        var handler = new GetEmployeeListHandler(db, _userManager.Object);
        var query = new GetEmployeeListQuery(
            new EmployeeListQuery { Q = "hokanson" }, null, true);

        // Act
        var result = await handler.Handle(query, CancellationToken.None);

        // Assert (Phase 3 F7-broad / WU-22 — paged envelope)
        result.Items.Should().HaveCount(1);
        result.Items.First().LastName.Should().Be("Hokanson");
    }

    [Fact]
    public async Task Handle_FilterByRole_ReturnsOnlyMatchingRole()
    {
        // Arrange
        using var db = TestDbContextFactory.Create();
        db.Users.AddRange(
            new ApplicationUser { Id = 1, FirstName = "Admin", LastName = "User", Initials = "AU", AvatarColor = "#4f46e5", Email = "admin@test.com", UserName = "admin", IsActive = true },
            new ApplicationUser { Id = 2, FirstName = "Engineer", LastName = "User", Initials = "EU", AvatarColor = "#ef4444", Email = "eng@test.com", UserName = "eng", IsActive = true });
        await db.SaveChangesAsync();

        _userManager.Setup(u => u.GetRolesAsync(It.Is<ApplicationUser>(u => u.Id == 1)))
            .ReturnsAsync(["Admin"]);
        _userManager.Setup(u => u.GetRolesAsync(It.Is<ApplicationUser>(u => u.Id == 2)))
            .ReturnsAsync(["Engineer"]);

        var handler = new GetEmployeeListHandler(db, _userManager.Object);
        var query = new GetEmployeeListQuery(
            new EmployeeListQuery { Role = "Admin" }, null, true);

        // Act
        var result = await handler.Handle(query, CancellationToken.None);

        // Assert (Phase 3 F7-broad / WU-22 — paged envelope)
        result.Items.Should().HaveCount(1);
        result.Items.First().FirstName.Should().Be("Admin");
    }

    [Fact]
    public async Task Handle_ExcludesServiceIdentities_FromRosterAndCount()
    {
        // Arrange
        using var db = TestDbContextFactory.Create();
        var human = new ApplicationUser { Id = 1, FirstName = "Dan", LastName = "Hokanson", Initials = "DH", AvatarColor = "#4f46e5", Email = "dan@test.com", UserName = "dan", IsActive = true };
        var service = new ApplicationUser { Id = 2, FirstName = "Lead Intake", LastName = "Service", Initials = "LI", AvatarColor = "#64748b", Email = "lead-intake-system@forge.local", UserName = "lead-intake-system@forge.local", IsActive = true };
        db.Users.AddRange(human, service);
        await db.SaveChangesAsync();

        _userManager.Setup(u => u.GetRolesAsync(It.IsAny<ApplicationUser>()))
            .ReturnsAsync(["Engineer"]);
        _userManager.Setup(u => u.GetUsersInRoleAsync(SeedData.LeadIntakeRoleName))
            .ReturnsAsync([service]);

        var handler = new GetEmployeeListHandler(db, _userManager.Object);
        var query = new GetEmployeeListQuery(new EmployeeListQuery(), null, true);

        // Act
        var result = await handler.Handle(query, CancellationToken.None);

        // Assert — service identity is absent from both the page and the total.
        result.Items.Should().HaveCount(1);
        result.Items.First().LastName.Should().Be("Hokanson");
        result.TotalCount.Should().Be(1);
    }
}
