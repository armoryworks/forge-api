using FluentAssertions;
using MediatR;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

using Forge.Api.Features.Notifications;
using Forge.Api.Jobs;
using Forge.Core.Entities;
using Forge.Core.Enums;
using Forge.Core.Interfaces;
using Forge.Data.Context;
using Forge.Tests.Helpers;

namespace Forge.Tests.Jobs;

public class LeadFollowUpReminderJobTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 8, 14, 0, 0, TimeSpan.Zero);

    private readonly AppDbContext _db = TestDbContextFactory.Create();
    private readonly Mock<ISender> _mediator = new();
    private readonly List<int> _notifiedUserIds = [];

    public LeadFollowUpReminderJobTests()
    {
        _mediator.Setup(m => m.Send(It.IsAny<CreateNotificationCommand>(), It.IsAny<CancellationToken>()))
            .Callback<IRequest<Forge.Core.Models.NotificationResponseModel>, CancellationToken>((c, _) =>
                _notifiedUserIds.Add(((CreateNotificationCommand)c).Data.UserId));
    }

    private Task RunAsync() =>
        new LeadFollowUpReminderJob(_db, _mediator.Object, Mock.Of<IClock>(c => c.UtcNow == Now),
            NullLogger<LeadFollowUpReminderJob>.Instance).RunAsync();

    [Fact]
    public async Task The_assigned_rep_is_reminded_and_the_creator_only_when_the_lead_has_no_owner()
    {
        _db.Leads.AddRange(
            new Lead { Id = 1, CompanyName = "Owned", CreatedBy = 7, AssignedToUserId = 12, FollowUpDate = Now.AddHours(2) },
            new Lead { Id = 2, CompanyName = "Unowned", CreatedBy = 7, FollowUpDate = Now.AddHours(20) },
            new Lead { Id = 3, CompanyName = "Imported before the fix", CreatedBy = 0, FollowUpDate = Now.AddHours(2) },
            new Lead { Id = 4, CompanyName = "Later", CreatedBy = 7, AssignedToUserId = 12, FollowUpDate = Now.AddDays(5) },
            new Lead { Id = 5, CompanyName = "Lost", CreatedBy = 7, Status = LeadStatus.Lost, FollowUpDate = Now.AddHours(2) });
        await _db.SaveChangesAsync();

        await RunAsync();

        _notifiedUserIds.Should().BeEquivalentTo([12, 7]);
    }
}
