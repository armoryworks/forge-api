using FluentAssertions;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

using Forge.Api.Features.Training;
using Forge.Core.Entities;
using Forge.Core.Enums;
using Forge.Core.Models;
using Forge.Tests.Helpers;

namespace Forge.Tests.Handlers.Training;

public class PathEnrollmentTests
{
    private readonly TimerTestHarness _h = new();

    private async Task<(TrainingPath Path, int ModuleA)> SeedPathAsync()
    {
        var a = new TrainingModule { Title = "Lockout/tagout", Slug = "loto", Summary = "s" };
        var b = new TrainingModule { Title = "Forklift", Slug = "forklift", Summary = "s" };
        _h.Db.TrainingModules.AddRange(a, b);
        var path = new TrainingPath { Title = "Floor safety", Slug = "floor-safety", Description = "d" };
        _h.Db.TrainingPaths.Add(path);
        await _h.Db.SaveChangesAsync();
        _h.Db.TrainingPathModules.AddRange(
            new TrainingPathModule { PathId = path.Id, ModuleId = a.Id, Position = 1 },
            new TrainingPathModule { PathId = path.Id, ModuleId = b.Id, Position = 2 });
        await _h.Db.SaveChangesAsync();
        return (path, a.Id);
    }

    private async Task GrantAsync(int userId, string role)
    {
        var r = await _h.Db.Roles.FirstOrDefaultAsync(x => x.Name == role);
        if (r is null)
        {
            r = new IdentityRole<int> { Name = role, NormalizedName = role.ToUpperInvariant() };
            _h.Db.Roles.Add(r);
            await _h.Db.SaveChangesAsync();
        }
        _h.Db.UserRoles.Add(new IdentityUserRole<int> { UserId = userId, RoleId = r.Id });
        await _h.Db.SaveChangesAsync();
    }

    [Fact]
    public async Task Enrolling_a_role_and_a_person_enrols_each_once_with_the_due_date()
    {
        var (path, _) = await SeedPathAsync();
        var admin = await _h.AddUserAsync();
        var worker1 = await _h.AddUserAsync();
        var worker2 = await _h.AddUserAsync();
        var engineer = await _h.AddUserAsync();
        await GrantAsync(worker1.Id, "ProductionWorker");
        await GrantAsync(worker2.Id, "ProductionWorker");
        var due = new DateOnly(2026, 11, 1);

        var result = await new EnrollInPathHandler(_h.Db).Handle(new EnrollInPathCommand(path.Id,
            new EnrollInPathRequestModel([engineer.Id, worker1.Id], ["ProductionWorker"], due), admin.Id), CancellationToken.None);

        result.Should().Be(new EnrollInPathResponseModel(3, 0));
        var rows = await _h.Db.TrainingPathEnrollments.Where(e => e.PathId == path.Id).ToListAsync();
        rows.Select(e => e.UserId).Should().BeEquivalentTo([worker1.Id, worker2.Id, engineer.Id]);
        rows.Should().OnlyContain(e => e.DueDate == due && e.AssignedByUserId == admin.Id);

        var again = await new EnrollInPathHandler(_h.Db).Handle(new EnrollInPathCommand(path.Id,
            new EnrollInPathRequestModel(null, ["ProductionWorker"], null), admin.Id), CancellationToken.None);
        again.Should().Be(new EnrollInPathResponseModel(0, 2));
    }

    [Fact]
    public async Task A_removed_enrolment_is_restored_on_re_enrol()
    {
        var (path, _) = await SeedPathAsync();
        var worker = await _h.AddUserAsync();
        await new EnrollInPathHandler(_h.Db).Handle(new EnrollInPathCommand(path.Id,
            new EnrollInPathRequestModel([worker.Id], null, null), 0), CancellationToken.None);
        var enrollment = await _h.Db.TrainingPathEnrollments.SingleAsync();

        await new RemoveTrainingEnrollmentHandler(_h.Db, _h.Clock.Object).Handle(new RemoveTrainingEnrollmentCommand(enrollment.Id), CancellationToken.None);
        (await _h.Db.TrainingPathEnrollments.CountAsync()).Should().Be(0);

        await new EnrollInPathHandler(_h.Db).Handle(new EnrollInPathCommand(path.Id,
            new EnrollInPathRequestModel([worker.Id], null, new DateOnly(2026, 12, 1)), 0), CancellationToken.None);

        var restored = await _h.Db.TrainingPathEnrollments.SingleAsync();
        restored.Id.Should().Be(enrollment.Id);
        restored.DueDate.Should().Be(new DateOnly(2026, 12, 1));
    }

    [Fact]
    public async Task Enrolment_list_reports_progress_and_overdue()
    {
        var (path, moduleA) = await SeedPathAsync();
        var worker = await _h.AddUserAsync();
        _h.Db.TrainingPathEnrollments.Add(new TrainingPathEnrollment { UserId = worker.Id, PathId = path.Id, DueDate = new DateOnly(2026, 10, 1) });
        _h.Db.TrainingProgress.Add(new TrainingProgress { UserId = worker.Id, ModuleId = moduleA, Status = TrainingProgressStatus.Completed });
        await _h.Db.SaveChangesAsync();

        var rows = await new GetPathEnrollmentsHandler(_h.Db, _h.Clock.Object).Handle(new GetPathEnrollmentsQuery(path.Id), CancellationToken.None);

        var row = rows.Should().ContainSingle().Subject;
        row.TotalModules.Should().Be(2);
        row.CompletedModules.Should().Be(1);
        row.ProgressPercent.Should().Be(50);
        row.IsOverdue.Should().BeTrue();
        row.UserName.Should().Be("Machinist, Pat");
    }

    [Fact]
    public async Task Changing_the_due_date_updates_the_enrolment()
    {
        var (path, _) = await SeedPathAsync();
        var worker = await _h.AddUserAsync();
        var enrollment = new TrainingPathEnrollment { UserId = worker.Id, PathId = path.Id };
        _h.Db.TrainingPathEnrollments.Add(enrollment);
        await _h.Db.SaveChangesAsync();

        await new UpdateTrainingEnrollmentHandler(_h.Db).Handle(
            new UpdateTrainingEnrollmentCommand(enrollment.Id, new DateOnly(2027, 1, 15)), CancellationToken.None);

        enrollment.DueDate.Should().Be(new DateOnly(2027, 1, 15));
    }
}
