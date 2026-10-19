using FluentAssertions;
using Microsoft.AspNetCore.Identity;
using Moq;

using Forge.Api.Features.Employees;
using Forge.Core.Entities;
using Forge.Data.Context;
using Forge.Tests.Helpers;

namespace Forge.Tests.Handlers.Employees;

public class EmployeeTeamScopeTests
{
    private const int AdminId = 1;
    private const int TeamManagerId = 2;
    private const int TeamlessManagerId = 3;
    private const int Teammate = 10;
    private const int Outsider = 11;

    private readonly AppDbContext _db = TestDbContextFactory.Create();
    private readonly GetEmployeeDetailHandler _detail;

    public EmployeeTeamScopeTests()
    {
        var userManager = new Mock<UserManager<ApplicationUser>>(
            Mock.Of<IUserStore<ApplicationUser>>(), null!, null!, null!, null!, null!, null!, null!, null!);
        userManager.Setup(u => u.GetRolesAsync(It.IsAny<ApplicationUser>())).ReturnsAsync(["Engineer"]);
        _detail = new GetEmployeeDetailHandler(_db, userManager.Object);

        _db.Teams.AddRange(new Team { Id = 7, Name = "Molding" }, new Team { Id = 8, Name = "Assembly" });
        _db.Users.AddRange(
            User(AdminId, null),
            User(TeamManagerId, 7),
            User(TeamlessManagerId, null),
            User(Teammate, 7),
            User(Outsider, 8));
        _db.EmployeeProfiles.Add(new EmployeeProfile { Id = 40, UserId = null, FirstName = "No", LastName = "Login" });
        _db.SaveChanges();
    }

    private static ApplicationUser User(int id, int? teamId) => new()
    {
        Id = id, FirstName = $"F{id}", LastName = $"L{id}", Email = $"u{id}@test.com", UserName = $"u{id}",
        IsActive = true, TeamId = teamId,
    };

    private Task<bool> CanView(int target, int caller, bool isAdmin) =>
        new CanViewEmployeeHandler(_db).Handle(new CanViewEmployeeQuery(target, caller, isAdmin), CancellationToken.None);

    [Fact]
    public async Task ManagerOutsideTheTeam_CannotViewSubResources()
    {
        (await CanView(Outsider, TeamManagerId, false)).Should().BeFalse();
        (await CanView(Teammate, TeamManagerId, false)).Should().BeTrue();
    }

    [Fact]
    public async Task Admin_AndTeamlessManager_AreUnrestricted()
    {
        (await CanView(Outsider, AdminId, true)).Should().BeTrue();
        (await CanView(Outsider, TeamlessManagerId, false)).Should().BeTrue();
    }

    [Fact]
    public async Task Detail_ManagerOutsideTheTeam_IsNotFound()
    {
        var result = await _detail.Handle(
            new GetEmployeeDetailQuery(Outsider, EmployeeKinds.User, TeamManagerId, false), CancellationToken.None);

        result.Should().BeNull();
    }

    [Fact]
    public async Task Detail_Teammate_IsVisible_WithTheTeamName()
    {
        var result = await _detail.Handle(
            new GetEmployeeDetailQuery(Teammate, EmployeeKinds.User, TeamManagerId, false), CancellationToken.None);

        result!.TeamName.Should().Be("Molding");
    }

    [Fact]
    public async Task Detail_UserlessProfile_IsHiddenFromATeamManager_ButNotFromAdmin()
    {
        (await _detail.Handle(new GetEmployeeDetailQuery(40, EmployeeKinds.Profile, TeamManagerId, false), CancellationToken.None))
            .Should().BeNull();
        (await _detail.Handle(new GetEmployeeDetailQuery(40, EmployeeKinds.Profile, AdminId, true), CancellationToken.None))
            .Should().NotBeNull();
    }
}
