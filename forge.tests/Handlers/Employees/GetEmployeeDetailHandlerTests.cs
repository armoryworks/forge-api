using FluentAssertions;
using Microsoft.AspNetCore.Identity;
using Moq;

using Forge.Api.Features.Employees;
using Forge.Core.Entities;
using Forge.Data.Context;
using Forge.Tests.Helpers;

namespace Forge.Tests.Handlers.Employees;

public class GetEmployeeDetailHandlerTests
{
    private readonly AppDbContext _db = TestDbContextFactory.Create();
    private readonly GetEmployeeDetailHandler _handler;

    public GetEmployeeDetailHandlerTests()
    {
        var userManager = new Mock<UserManager<ApplicationUser>>(
            Mock.Of<IUserStore<ApplicationUser>>(), null!, null!, null!, null!, null!, null!, null!, null!);
        userManager.Setup(u => u.GetRolesAsync(It.IsAny<ApplicationUser>())).ReturnsAsync(["Engineer"]);
        _handler = new GetEmployeeDetailHandler(_db, userManager.Object);

        _db.Users.Add(new ApplicationUser { Id = 5, FirstName = "Una", LastName = "User", Email = "una@test.com", UserName = "una", IsActive = true });
        _db.EmployeeProfiles.Add(new EmployeeProfile { Id = 5, UserId = null, FirstName = "Pat", LastName = "Profile" });
        _db.SaveChanges();
    }

    [Fact]
    public async Task KindUser_ReturnsTheUserEvenWhenAUserlessProfileSharesTheId()
    {
        var result = await _handler.Handle(new GetEmployeeDetailQuery(5, EmployeeKinds.User), CancellationToken.None);

        result!.LastName.Should().Be("User");
        result.HasLogin.Should().BeTrue();
    }

    [Fact]
    public async Task KindProfile_ReturnsTheUserlessProfile()
    {
        var result = await _handler.Handle(new GetEmployeeDetailQuery(5, EmployeeKinds.Profile), CancellationToken.None);

        result!.LastName.Should().Be("Profile");
        result.HasLogin.Should().BeFalse();
    }

    [Fact]
    public async Task KindUser_NoSuchUser_IsNotFoundEvenWhenAProfileMatches()
    {
        _db.EmployeeProfiles.Add(new EmployeeProfile { Id = 9, UserId = null, FirstName = "Only", LastName = "Profile" });
        await _db.SaveChangesAsync();

        var result = await _handler.Handle(new GetEmployeeDetailQuery(9, EmployeeKinds.User), CancellationToken.None);

        result.Should().BeNull();
    }

    [Fact]
    public async Task NoKind_KeepsTheLegacyOrder()
    {
        var result = await _handler.Handle(new GetEmployeeDetailQuery(5), CancellationToken.None);

        result!.LastName.Should().Be("Profile");
    }
}
