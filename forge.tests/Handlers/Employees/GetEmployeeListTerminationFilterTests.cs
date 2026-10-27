using FluentAssertions;
using Microsoft.AspNetCore.Identity;
using Moq;

using Forge.Api.Features.Employees;
using Forge.Core.Entities;
using Forge.Core.Models;
using Forge.Data.Context;
using Forge.Tests.Helpers;

namespace Forge.Tests.Handlers.Employees;

public class GetEmployeeListTerminationFilterTests
{
    private static async Task<(AppDbContext Db, GetEmployeeListHandler Handler)> SeedAsync()
    {
        var db = TestDbContextFactory.Create();
        db.Users.AddRange(
            new ApplicationUser { Id = 1, FirstName = "Ava", LastName = "Active", Email = "a@test.com", UserName = "a", IsActive = true },
            new ApplicationUser { Id = 2, FirstName = "Ian", LastName = "Inactive", Email = "i@test.com", UserName = "i", IsActive = false },
            new ApplicationUser { Id = 3, FirstName = "Tom", LastName = "Terminated", Email = "t@test.com", UserName = "t", IsActive = false });
        db.EmployeeProfiles.AddRange(
            new EmployeeProfile { UserId = 3, TerminationDate = new DateOnly(2026, 10, 1), TerminationReasonCode = "layoff", TerminationAppliedAt = DateTimeOffset.UtcNow },
            new EmployeeProfile { FirstName = "Una", LastName = "Userless" },
            new EmployeeProfile { FirstName = "Gil", LastName = "Gone", TerminationDate = new DateOnly(2026, 9, 1), TerminationReasonCode = "resignation", TerminationAppliedAt = DateTimeOffset.UtcNow });
        await db.SaveChangesAsync();

        var userManager = new Mock<UserManager<ApplicationUser>>(
            new Mock<IUserStore<ApplicationUser>>().Object, null!, null!, null!, null!, null!, null!, null!, null!);
        userManager.Setup(u => u.GetUsersInRoleAsync(It.IsAny<string>())).ReturnsAsync([]);
        userManager.Setup(u => u.GetRolesAsync(It.IsAny<ApplicationUser>())).ReturnsAsync(["Engineer"]);
        return (db, new GetEmployeeListHandler(db, userManager.Object));
    }

    private static async Task<List<string>> NamesAsync(GetEmployeeListHandler handler, EmployeeListQuery query)
        => (await handler.Handle(new GetEmployeeListQuery(query, null, true), CancellationToken.None))
            .Items.Select(i => i.LastName).ToList();

    [Fact]
    public async Task Terminated_only_returns_terminated_users_and_profiles_flagged()
    {
        var (db, handler) = await SeedAsync();
        using var _ = db;

        var result = await handler.Handle(new GetEmployeeListQuery(new EmployeeListQuery { Terminated = true }, null, true), CancellationToken.None);

        result.Items.Select(i => i.LastName).Should().BeEquivalentTo(["Terminated", "Gone"]);
        result.Items.Should().OnlyContain(i => i.IsTerminated && i.TerminationDate != null && !i.IsActive);
    }

    [Fact]
    public async Task Inactive_but_not_terminated_excludes_terminated_people()
    {
        var (db, handler) = await SeedAsync();
        using var _ = db;

        (await NamesAsync(handler, new EmployeeListQuery { IsActive = false, Terminated = false })).Should().Equal("Inactive");
    }

    [Fact]
    public async Task Active_leaves_out_terminated_userless_profiles()
    {
        var (db, handler) = await SeedAsync();
        using var _ = db;

        (await NamesAsync(handler, new EmployeeListQuery { IsActive = true })).Should().BeEquivalentTo(["Active", "Userless"]);
    }
}
