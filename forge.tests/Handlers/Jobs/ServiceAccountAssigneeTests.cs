using FluentAssertions;
using Microsoft.AspNetCore.Identity;

using Forge.Api.Features.Jobs;
using Forge.Api.Features.Users;
using Forge.Data.Context;
using Forge.Data.Repositories;
using Forge.Tests.Helpers;

namespace Forge.Tests.Handlers.Jobs;

public class ServiceAccountAssigneeTests
{
    private const int PersonId = 1;
    private const int ServiceId = 2;

    private static AppDbContext Seed()
    {
        var db = TestDbContextFactory.Create();
        db.Roles.Add(new IdentityRole<int> { Id = 50, Name = ServiceAccounts.LeadIntakeRole, NormalizedName = "LEADINTAKE" });
        db.Roles.Add(new IdentityRole<int> { Id = 51, Name = "PM", NormalizedName = "PM" });
        db.Users.Add(new ApplicationUser { Id = PersonId, Email = "pm@forge.local", UserName = "pm@forge.local", FirstName = "Pat", LastName = "Morris", IsActive = true });
        db.Users.Add(new ApplicationUser { Id = ServiceId, Email = "lead-intake-system@forge.local", UserName = "lead-intake-system@forge.local", FirstName = "Lead Intake", LastName = "Service", IsActive = true });
        db.UserRoles.Add(new IdentityUserRole<int> { UserId = PersonId, RoleId = 51 });
        db.UserRoles.Add(new IdentityUserRole<int> { UserId = ServiceId, RoleId = 50 });
        db.SaveChanges();
        return db;
    }

    [Fact]
    public async Task UserList_UsedByAssigneePickers_ExcludesServiceAccounts()
    {
        var db = Seed();

        var users = await new GetUsersHandler(new UserRepository(db), db, StubCapabilitySnapshotProvider.Off)
            .Handle(new GetUsersQuery(), default);

        users.Select(u => u.Id).Should().Equal(PersonId);
    }

    [Fact]
    public async Task NameLookup_ExcludesServiceAccounts()
    {
        var db = Seed();

        var users = await new UserRepository(db).FindByNamesAsync(["Lead Intake", "leadintakeservice", "Pat"], (_, _) => true, default);

        users.Select(u => u.Id).Should().Equal(PersonId);
    }

    [Fact]
    public async Task AssigningAServiceAccount_IsRejected_EvenWithHrTrackingOff()
    {
        var db = Seed();

        var act = () => AssigneeComplianceCheck.EnsureCanBeAssigned(db, StubCapabilitySnapshotProvider.Off, ServiceId, default);

        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("*Service accounts*");
        await AssigneeComplianceCheck.EnsureCanBeAssigned(db, StubCapabilitySnapshotProvider.Off, PersonId, default);
    }

    [Fact]
    public void IsServiceAccount_RecognisesTheLeadIntakeRole()
    {
        ServiceAccounts.IsServiceAccount(["LeadIntake"]).Should().BeTrue();
        ServiceAccounts.IsServiceAccount(["PM", "Admin"]).Should().BeFalse();
    }
}
