using FluentAssertions;
using Microsoft.AspNetCore.Identity;
using Moq;

using Forge.Api.Features.Employees;
using Forge.Core.Entities;
using Forge.Core.Enums;
using Forge.Data.Context;
using Forge.Tests.Helpers;

namespace Forge.Tests.Handlers.Employees;

public class EmployeeComplianceAndStatsTests
{
    private readonly AppDbContext _db = TestDbContextFactory.Create();

    public EmployeeComplianceAndStatsTests()
    {
        _db.Users.Add(new ApplicationUser { Id = 15, FirstName = "Jun", LastName = "Sato", Email = "jun@test.com", UserName = "jun", IsActive = true });
        _db.SaveChanges();
    }

    [Fact]
    public async Task StatsAndDetail_UseTheSameComplianceDefinition()
    {
        _db.EmployeeProfiles.Add(new EmployeeProfile
        {
            Id = 1, UserId = 15, FirstName = "Jun", LastName = "Sato",
            EmergencyContactName = "Kai", Street1 = "1 Main St",
            W4CompletedAt = DateTimeOffset.UtcNow,
        });
        await _db.SaveChangesAsync();

        var userManager = new Mock<UserManager<ApplicationUser>>(
            Mock.Of<IUserStore<ApplicationUser>>(), null!, null!, null!, null!, null!, null!, null!, null!);
        userManager.Setup(u => u.GetRolesAsync(It.IsAny<ApplicationUser>())).ReturnsAsync(["Engineer"]);

        var detail = await new GetEmployeeDetailHandler(_db, userManager.Object)
            .Handle(new GetEmployeeDetailQuery(15, EmployeeKinds.User), CancellationToken.None);
        var stats = await new GetEmployeeStatsHandler(_db).Handle(new GetEmployeeStatsQuery(15), CancellationToken.None);

        detail!.ComplianceCompletedItems.Should().Be(1);
        detail.ComplianceChecklist!.Should().HaveCount(detail.ComplianceTotalItems);
        detail.ComplianceChecklist!.Single(c => c.Key == "emergencyContact").Completed.Should().BeFalse();
        detail.ComplianceChecklist!.Single(c => c.Key == "address").Completed.Should().BeFalse();
        stats.CompliancePercent.Should().Be(
            (int)Math.Round(100.0 * detail.ComplianceCompletedItems / detail.ComplianceTotalItems));
    }

    [Fact]
    public async Task TrainingPercent_IsMeasuredAgainstEnrolledRequiredModules()
    {
        var path = new TrainingPath { Id = 1, Title = "Onboarding", Slug = "onboarding" };
        for (var i = 1; i <= 4; i++)
        {
            _db.TrainingModules.Add(new TrainingModule { Id = i, Title = $"M{i}", Slug = $"m{i}" });
            path.PathModules.Add(new TrainingPathModule { ModuleId = i, Position = i, IsRequired = true });
        }
        _db.TrainingPaths.Add(path);
        _db.TrainingPathEnrollments.Add(new TrainingPathEnrollment { UserId = 15, PathId = 1 });
        _db.TrainingProgress.AddRange(
            new TrainingProgress { UserId = 15, ModuleId = 1, Status = TrainingProgressStatus.Completed },
            new TrainingProgress { UserId = 15, ModuleId = 2, Status = TrainingProgressStatus.InProgress });
        await _db.SaveChangesAsync();

        var stats = await new GetEmployeeStatsHandler(_db).Handle(new GetEmployeeStatsQuery(15), CancellationToken.None);

        stats.TrainingProgressPercent.Should().Be(25);
    }
}
