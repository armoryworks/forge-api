using FluentAssertions;

using Forge.Api.Features.Jobs;
using Forge.Api.Features.Onboarding;
using Forge.Core.Entities;
using Forge.Data.Context;
using Forge.Tests.Helpers;

namespace Forge.Tests.Compliance;

public class NonEmployeeExclusionTests
{
    private static readonly StubCapabilitySnapshotProvider HrOn = new("CAP-HR-HIRE");

    private static ApplicationUser SeedUser(AppDbContext db, int id, bool nonEmployee)
    {
        var user = new ApplicationUser
        {
            Id = id,
            Email = $"user{id}@forge.local",
            UserName = $"user{id}@forge.local",
            FirstName = "Test",
            LastName = "User",
            IsNonEmployee = nonEmployee,
        };
        db.Users.Add(user);
        return user;
    }

    [Fact]
    public async Task NonEmployee_With_No_Profile_Can_Be_Assigned()
    {
        var db = TestDbContextFactory.Create();
        SeedUser(db, 1, nonEmployee: true);
        await db.SaveChangesAsync();

        var act = async () => await AssigneeComplianceCheck.EnsureCanBeAssigned(db, HrOn, 1, default);

        await act.Should().NotThrowAsync();
    }

    [Fact]
    public async Task Employee_With_No_Profile_Is_Still_Blocked()
    {
        var db = TestDbContextFactory.Create();
        SeedUser(db, 2, nonEmployee: false);
        await db.SaveChangesAsync();

        var act = async () => await AssigneeComplianceCheck.EnsureCanBeAssigned(db, HrOn, 2, default);

        await act.Should().ThrowAsync<InvalidOperationException>();
    }

    [Fact]
    public async Task NonEmployee_Is_Not_Prompted_For_Onboarding()
    {
        var db = TestDbContextFactory.Create();
        SeedUser(db, 3, nonEmployee: true);
        await db.SaveChangesAsync();

        var status = await new GetOnboardingStatusHandler(db, HrOn)
            .Handle(new GetOnboardingStatusQuery(3), default);

        status.AllComplete.Should().BeTrue();
        status.CanBeAssigned.Should().BeTrue();
    }

    [Fact]
    public async Task Clearing_The_Flag_Restores_The_Real_Compliance_State()
    {
        var db = TestDbContextFactory.Create();
        var user = SeedUser(db, 4, nonEmployee: true);
        db.EmployeeProfiles.Add(new EmployeeProfile
        {
            UserId = 4,
            W4CompletedAt = DateTime.UtcNow,
        });
        await db.SaveChangesAsync();

        var suppressed = await new GetOnboardingStatusHandler(db, HrOn)
            .Handle(new GetOnboardingStatusQuery(4), default);
        suppressed.AllComplete.Should().BeTrue();

        user.IsNonEmployee = false;
        await db.SaveChangesAsync();

        var restored = await new GetOnboardingStatusHandler(db, HrOn)
            .Handle(new GetOnboardingStatusQuery(4), default);

        restored.W4Complete.Should().BeTrue();
        restored.I9Complete.Should().BeFalse();
        restored.AllComplete.Should().BeFalse();
    }
}
