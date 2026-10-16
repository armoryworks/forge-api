using FluentAssertions;

using Microsoft.AspNetCore.Identity;

using Moq;

using Forge.Api.Features.ShopFloor;
using Forge.Api.Features.Training;
using Forge.Core.Interfaces;
using Forge.Data.Context;
using Forge.Tests.Helpers;

namespace Forge.Tests.Handlers.Users;

public class ServiceAccountPeopleListTests
{
    private const int PersonId = 1;
    private const int ServiceId = 2;

    private static AppDbContext Seed()
    {
        var db = TestDbContextFactory.Create();
        db.Roles.Add(new IdentityRole<int> { Id = 50, Name = ServiceAccounts.LeadIntakeRole, NormalizedName = "LEADINTAKE" });
        db.Roles.Add(new IdentityRole<int> { Id = 51, Name = "ProductionWorker", NormalizedName = "PRODUCTIONWORKER" });
        db.Users.Add(new ApplicationUser { Id = PersonId, Email = "worker@forge.local", UserName = "worker@forge.local", FirstName = "Bea", LastName = "Kelly", IsActive = true });
        db.Users.Add(new ApplicationUser { Id = ServiceId, Email = "lead-intake-system@forge.local", UserName = "lead-intake-system@forge.local", FirstName = "Lead Intake", LastName = "Service", IsActive = true });
        db.UserRoles.Add(new IdentityUserRole<int> { UserId = PersonId, RoleId = 51 });
        db.UserRoles.Add(new IdentityUserRole<int> { UserId = ServiceId, RoleId = 50 });
        db.SaveChanges();
        return db;
    }

    private static Mock<UserManager<ApplicationUser>> UserManager()
    {
        var manager = new Mock<UserManager<ApplicationUser>>(
            Mock.Of<IUserStore<ApplicationUser>>(), null!, null!, null!, null!, null!, null!, null!, null!);
        manager.Setup(m => m.GetRolesAsync(It.IsAny<ApplicationUser>())).ReturnsAsync(new List<string>());
        return manager;
    }

    [Fact]
    public async Task ShopFloorClockBoard_ExcludesServiceAccounts()
    {
        var db = Seed();
        var clockEventTypes = new Mock<IClockEventTypeService>();
        clockEventTypes.Setup(s => s.GetAllAsync(It.IsAny<CancellationToken>())).ReturnsAsync([]);
        var clock = new Mock<IClock>();
        clock.Setup(c => c.UtcNow).Returns(DateTimeOffset.UtcNow);

        var workers = await new GetClockStatusHandler(db, UserManager().Object, clockEventTypes.Object, Mock.Of<IJobOperationService>(), clock.Object)
            .Handle(new GetClockStatusQuery(), default);

        workers.Select(w => w.UserId).Should().Equal(PersonId);
    }

    [Fact]
    public async Task TrainingProgressSummary_ExcludesServiceAccounts()
    {
        var db = Seed();

        var rows = await new GetAdminProgressSummaryHandler(db, UserManager().Object)
            .Handle(new GetAdminProgressSummaryQuery(), default);

        rows.Select(r => r.UserId).Should().Equal(PersonId);
    }
}
