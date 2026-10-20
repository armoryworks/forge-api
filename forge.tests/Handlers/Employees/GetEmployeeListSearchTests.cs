using FluentAssertions;
using Microsoft.AspNetCore.Identity;
using Moq;

using Forge.Api.Features.Employees;
using Forge.Core.Entities;
using Forge.Core.Models;
using Forge.Data.Context;
using Forge.Tests.Helpers;

namespace Forge.Tests.Handlers.Employees;

public class GetEmployeeListSearchTests
{
    private readonly AppDbContext _db = TestDbContextFactory.Create();
    private readonly GetEmployeeListHandler _handler;

    public GetEmployeeListSearchTests()
    {
        var userManager = new Mock<UserManager<ApplicationUser>>(
            Mock.Of<IUserStore<ApplicationUser>>(), null!, null!, null!, null!, null!, null!, null!, null!);
        userManager.Setup(u => u.GetUsersInRoleAsync(It.IsAny<string>())).ReturnsAsync([]);
        userManager.Setup(u => u.GetRolesAsync(It.IsAny<ApplicationUser>())).ReturnsAsync(["Engineer"]);
        _handler = new GetEmployeeListHandler(_db, userManager.Object);

        _db.Teams.Add(new Team { Id = 3, Name = "Molding Cell" });
        _db.Users.AddRange(
            new ApplicationUser { Id = 1, FirstName = "Ben", LastName = "Kelly", Email = "bk@test.com", UserName = "bk", IsActive = true, TeamId = 3 },
            new ApplicationUser { Id = 2, FirstName = "Ben", LastName = "Ortiz", Email = "bo@test.com", UserName = "bo", IsActive = true });
        _db.EmployeeProfiles.Add(new EmployeeProfile { Id = 9, UserId = 1, JobTitle = "Machinist", Department = "Molding" });
        _db.SaveChanges();
    }

    private async Task<List<string>> Search(string q) =>
        (await _handler.Handle(new GetEmployeeListQuery(new EmployeeListQuery { Q = q }, null, true), CancellationToken.None))
            .Items.Select(i => i.LastName).ToList();

    [Theory]
    [InlineData("Ben Kelly")]
    [InlineData("Kelly, Ben")]
    [InlineData("machinist")]
    [InlineData("Molding")]
    public async Task FindsByFullName_DisplayedName_JobTitle_AndDepartment(string q)
    {
        (await Search(q)).Should().Equal("Kelly");
    }

    [Fact]
    public async Task TeamName_ComesFromTeams()
    {
        var result = await _handler.Handle(new GetEmployeeListQuery(new EmployeeListQuery(), null, true), CancellationToken.None);

        result.Items.Single(i => i.Id == 1).TeamName.Should().Be("Molding Cell");
    }
}
