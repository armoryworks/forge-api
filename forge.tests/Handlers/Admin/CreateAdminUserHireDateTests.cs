using FluentAssertions;
using Microsoft.AspNetCore.Identity;
using Moq;

using Forge.Api.Features.Admin;
using Forge.Core.Interfaces;
using Forge.Data.Context;
using Forge.Tests.Helpers;

namespace Forge.Tests.Handlers.Admin;

public class CreateAdminUserHireDateTests
{
    private readonly AppDbContext _db = TestDbContextFactory.Create();
    private readonly Mock<UserManager<ApplicationUser>> _userManager = new(
        Mock.Of<IUserStore<ApplicationUser>>(), null!, null!, null!, null!, null!, null!, null!, null!);

    public CreateAdminUserHireDateTests()
    {
        _userManager.Setup(x => x.CreateAsync(It.IsAny<ApplicationUser>()))
            .Callback((ApplicationUser u) =>
            {
                _db.Users.Add(u);
                _db.SaveChanges();
            })
            .ReturnsAsync(IdentityResult.Success);
        _userManager.Setup(x => x.AddToRolesAsync(It.IsAny<ApplicationUser>(), It.IsAny<IEnumerable<string>>()))
            .ReturnsAsync(IdentityResult.Success);
        _userManager.Setup(x => x.UpdateAsync(It.IsAny<ApplicationUser>()))
            .ReturnsAsync(IdentityResult.Success);
    }

    private CreateAdminUserHandler Handler() => new(_userManager.Object, new Mock<IBarcodeService>().Object, _db);

    [Fact]
    public async Task A_hire_date_on_create_becomes_the_employee_start_date()
    {
        var hired = new DateTimeOffset(2019, 9, 16, 0, 0, 0, TimeSpan.Zero);

        var result = await Handler().Handle(
            new CreateAdminUserCommand("ben@example.com", "Ben", "Ortiz", null, null, ["ProductionWorker"], hired),
            CancellationToken.None);

        _db.EmployeeProfiles.Single(p => p.UserId == result.Id).StartDate.Should().Be(hired);
    }

    [Fact]
    public async Task No_hire_date_on_create_leaves_no_profile()
    {
        var result = await Handler().Handle(
            new CreateAdminUserCommand("rosa@example.com", "Rosa", "Diaz", null, null, ["Engineer"]),
            CancellationToken.None);

        _db.EmployeeProfiles.Any(p => p.UserId == result.Id).Should().BeFalse();
    }
}
