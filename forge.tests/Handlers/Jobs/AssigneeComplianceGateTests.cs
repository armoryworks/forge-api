using FluentAssertions;

using Forge.Api.Features.EmployeeProfile;
using Forge.Api.Features.Jobs;
using Forge.Api.Features.Onboarding;
using Forge.Api.Features.Users;
using Forge.Core.Entities;
using Forge.Data.Context;
using Forge.Data.Repositories;
using Forge.Tests.Helpers;

namespace Forge.Tests.Handlers.Jobs;

public class AssigneeComplianceGateTests
{
    private const int UserId = 7;

    private static readonly StubCapabilitySnapshotProvider HrOn = new("CAP-HR-HIRE");
    private static readonly StubCapabilitySnapshotProvider HrOff = StubCapabilitySnapshotProvider.Off;

    private static AppDbContext SeedEmployee(string? workState, EmployeeProfile? profile)
    {
        var db = TestDbContextFactory.Create();

        db.ReferenceData.AddRange(
            new ReferenceData { GroupCode = "state_withholding", Code = "TX", Label = "Texas", Metadata = """{"category":"no_tax"}""" },
            new ReferenceData { GroupCode = "state_withholding", Code = "CA", Label = "California", Metadata = """{"category":"state_form","formName":"DE 4"}""" });

        CompanyLocation? location = null;
        if (workState is not null)
        {
            location = new CompanyLocation { Name = "Plant", Line1 = "1 Main", City = "Town", State = workState, PostalCode = "00000" };
            db.CompanyLocations.Add(location);
        }

        db.Users.Add(new ApplicationUser
        {
            Id = UserId,
            Email = "worker@forge.local",
            UserName = "worker@forge.local",
            FirstName = "Shop",
            LastName = "Worker",
            IsActive = true,
            WorkLocation = location,
        });

        if (profile is not null)
        {
            profile.UserId = UserId;
            db.EmployeeProfiles.Add(profile);
        }

        db.SaveChanges();
        return db;
    }

    private static EmployeeProfile FederalFormsAndEmergencyContact() => new()
    {
        W4CompletedAt = DateTimeOffset.UnixEpoch,
        I9CompletedAt = DateTimeOffset.UnixEpoch,
        EmergencyContactName = "Next of Kin",
        EmergencyContactPhone = "555-0100",
    };

    private static async Task<bool> ListedAsAssignable(AppDbContext db, StubCapabilitySnapshotProvider capabilities)
    {
        var users = await new GetUsersHandler(new UserRepository(db), db, capabilities)
            .Handle(new GetUsersQuery(), default);
        return users.Single(u => u.Id == UserId).CanBeAssignedJobs;
    }

    [Fact]
    public async Task HrOff_UserWithNoPaperwork_CanBeAssigned()
    {
        var db = SeedEmployee(workState: "CA", profile: null);

        var act = async () => await AssigneeComplianceCheck.EnsureCanBeAssigned(db, HrOff, UserId, default);

        await act.Should().NotThrowAsync();
        (await ListedAsAssignable(db, HrOff)).Should().BeTrue();
    }

    [Fact]
    public async Task HrOff_ProfileCountsAsComplete()
    {
        var db = SeedEmployee(workState: "CA", profile: null);

        var completeness = await new GetProfileCompletenessHandler(db, HrOff)
            .Handle(new GetProfileCompletenessQuery(UserId), default);
        var onboarding = await new GetOnboardingStatusHandler(db, HrOff)
            .Handle(new GetOnboardingStatusQuery(UserId), default);

        completeness.IsComplete.Should().BeTrue();
        completeness.CanBeAssignedJobs.Should().BeTrue();
        onboarding.AllComplete.Should().BeTrue();
        (await EmployeeComplianceRules.IsProfileCompleteAsync(db, HrOff, UserId, default)).Should().BeTrue();
    }

    [Fact]
    public async Task HrOn_NoTaxStateEmployeeWithoutStateForm_CanBeAssigned()
    {
        var db = SeedEmployee(workState: "TX", profile: FederalFormsAndEmergencyContact());

        var act = async () => await AssigneeComplianceCheck.EnsureCanBeAssigned(db, HrOn, UserId, default);

        await act.Should().NotThrowAsync();
        (await ListedAsAssignable(db, HrOn)).Should().BeTrue();

        var completeness = await new GetProfileCompletenessHandler(db, HrOn)
            .Handle(new GetProfileCompletenessQuery(UserId), default);
        completeness.CanBeAssignedJobs.Should().BeTrue();

        var onboarding = await new GetOnboardingStatusHandler(db, HrOn)
            .Handle(new GetOnboardingStatusQuery(UserId), default);
        onboarding.StateWithholdingComplete.Should().BeTrue();
        onboarding.CanBeAssigned.Should().BeTrue();
    }

    [Fact]
    public async Task HrOn_TaxedStateEmployeeWithoutStateForm_IsBlocked()
    {
        var db = SeedEmployee(workState: "CA", profile: FederalFormsAndEmergencyContact());

        var act = async () => await AssigneeComplianceCheck.EnsureCanBeAssigned(db, HrOn, UserId, default);

        await act.Should().ThrowAsync<InvalidOperationException>();
        (await ListedAsAssignable(db, HrOn)).Should().BeFalse();

        var onboarding = await new GetOnboardingStatusHandler(db, HrOn)
            .Handle(new GetOnboardingStatusQuery(UserId), default);
        onboarding.CanBeAssigned.Should().BeFalse();
    }

    [Fact]
    public async Task HrOn_UnknownStateWithoutStateForm_IsBlocked()
    {
        var db = SeedEmployee(workState: "ZZ", profile: FederalFormsAndEmergencyContact());

        var act = async () => await AssigneeComplianceCheck.EnsureCanBeAssigned(db, HrOn, UserId, default);

        await act.Should().ThrowAsync<InvalidOperationException>();
        (await ListedAsAssignable(db, HrOn)).Should().BeFalse();
    }

    [Fact]
    public async Task HrOn_CompanyStateFallback_AppliesNoTaxRule()
    {
        var db = SeedEmployee(workState: null, profile: FederalFormsAndEmergencyContact());
        db.SystemSettings.Add(new SystemSetting { Key = "company_state", Value = "TX" });
        await db.SaveChangesAsync();

        var act = async () => await AssigneeComplianceCheck.EnsureCanBeAssigned(db, HrOn, UserId, default);

        await act.Should().NotThrowAsync();
        (await ListedAsAssignable(db, HrOn)).Should().BeTrue();
    }

    [Fact]
    public async Task HrOn_NoTaxStateProfileComplete_DoesNotNeedStateForm()
    {
        var profile = FederalFormsAndEmergencyContact();
        profile.Street1 = "1 Main";
        profile.City = "Austin";
        profile.State = "TX";
        profile.ZipCode = "73301";
        profile.DirectDepositCompletedAt = DateTimeOffset.UnixEpoch;
        profile.WorkersCompAcknowledgedAt = DateTimeOffset.UnixEpoch;
        profile.HandbookAcknowledgedAt = DateTimeOffset.UnixEpoch;
        var db = SeedEmployee(workState: "TX", profile: profile);

        (await EmployeeComplianceRules.IsProfileCompleteAsync(db, HrOn, UserId, default)).Should().BeTrue();
    }

    [Theory]
    [InlineData(false, null, true)]
    [InlineData(true, "no_tax", true)]
    [InlineData(true, "state_form", false)]
    [InlineData(true, null, false)]
    public void CanBeAssignedJobs_AppliesStateRuleOnlyWhenHrIsOn(bool hrOn, string? stateCategory, bool expected)
    {
        EmployeeComplianceRules.CanBeAssignedJobs(FederalFormsAndEmergencyContact(), stateCategory, hrOn)
            .Should().Be(expected);
    }
}
