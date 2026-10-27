using FluentAssertions;
using MediatR;
using Moq;

using Forge.Api.Features.Notifications;
using Forge.Data.Context;
using Forge.Tests.Helpers;

namespace Forge.Tests.Handlers.Notifications;

public class AssignmentNotifierTests
{
    private readonly AppDbContext _db = TestDbContextFactory.Create();
    private readonly Mock<ISender> _sender = new();
    private readonly AssignmentNotifier _notifier;

    public AssignmentNotifierTests() => _notifier = new AssignmentNotifier(_db, _sender.Object);

    private int AddUser(bool active = true)
    {
        var user = new ApplicationUser { UserName = $"{Guid.NewGuid():N}@t.test", Email = "x@t.test", FirstName = "Kim", LastName = "Ray", IsActive = active };
        _db.Users.Add(user);
        _db.SaveChanges();
        return user.Id;
    }

    [Fact]
    public async Task Assigning_a_job_to_someone_else_sends_an_assignment_notification()
    {
        var actor = AddUser();
        var assignee = AddUser();
        _db.CurrentUserId = actor;

        await _notifier.JobAssignedAsync(3, "J-3", "Bracket", assignee, CancellationToken.None);

        _sender.Verify(s => s.Send(
            It.Is<CreateNotificationCommand>(c =>
                c.Data.UserId == assignee
                && c.Data.Type == "assignment"
                && c.Data.EntityType == "Job"
                && c.Data.EntityId == 3
                && c.Data.SenderId == actor
                && c.Data.Title.Contains("J-3")),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Assigning_to_yourself_sends_nothing()
    {
        var actor = AddUser();
        _db.CurrentUserId = actor;

        await _notifier.LeadAssignedAsync(4, "Acme", actor, CancellationToken.None);

        _sender.Verify(s => s.Send(It.IsAny<CreateNotificationCommand>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Inactive_users_are_not_notified()
    {
        var inactive = AddUser(active: false);

        await _notifier.JobAssignedAsync(3, "J-3", "Bracket", inactive, CancellationToken.None);

        _sender.Verify(s => s.Send(It.IsAny<CreateNotificationCommand>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Each_approver_is_notified_once()
    {
        var first = AddUser();
        var second = AddUser();

        await _notifier.ApprovalAssignedAsync("Expense", 12, "Travel — Mileage", [first, second, first], CancellationToken.None);

        _sender.Verify(s => s.Send(It.Is<CreateNotificationCommand>(c => c.Data.EntityType == "Expense" && c.Data.Message.Contains("Travel")), It.IsAny<CancellationToken>()), Times.Exactly(2));
    }
}
