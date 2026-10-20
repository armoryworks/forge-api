using FluentAssertions;
using Microsoft.AspNetCore.Identity;
using Moq;

using Forge.Api.Features.Admin;
using Forge.Data.Context;
using Forge.Tests.Helpers;

namespace Forge.Tests.Handlers.Admin;

public class GetAdminUsersOrderingTests
{
    [Fact]
    public async Task Users_AreOrderedByLastNameThenFirstName()
    {
        var db = TestDbContextFactory.Create();
        db.Users.AddRange(
            new ApplicationUser { Id = 1, UserName = "a@x.test", Email = "a@x.test", FirstName = "Zed", LastName = "Adams" },
            new ApplicationUser { Id = 2, UserName = "b@x.test", Email = "b@x.test", FirstName = "Amy", LastName = "Young" },
            new ApplicationUser { Id = 3, UserName = "c@x.test", Email = "c@x.test", FirstName = "Bea", LastName = "Adams" });
        await db.SaveChangesAsync();

        var userManager = new Mock<UserManager<ApplicationUser>>(
            Mock.Of<IUserStore<ApplicationUser>>(), null!, null!, null!, null!, null!, null!, null!, null!);
        userManager.Setup(x => x.GetRolesAsync(It.IsAny<ApplicationUser>())).ReturnsAsync(new List<string>());
        userManager.Setup(x => x.HasPasswordAsync(It.IsAny<ApplicationUser>())).ReturnsAsync(true);

        var handler = new GetAdminUsersHandler(db, userManager.Object, StubCapabilitySnapshotProvider.Off);
        var result = await handler.Handle(new GetAdminUsersQuery(), CancellationToken.None);

        result.Select(u => $"{u.LastName}, {u.FirstName}").Should().ContainInOrder("Adams, Bea", "Adams, Zed", "Young, Amy");
    }
}
