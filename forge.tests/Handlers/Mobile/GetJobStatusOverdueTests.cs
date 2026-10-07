using FluentAssertions;
using MediatR;
using Moq;

using Forge.Api.Features.Jobs;
using Forge.Api.Features.Mobile;
using Forge.Core.Interfaces;
using Forge.Core.Models;
using Forge.Tests.Helpers;

namespace Forge.Tests.Handlers.Mobile;

public class GetJobStatusOverdueTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 7, 3, 0, 0, TimeSpan.Zero);

    private static async Task<bool> IsOverdueAsync(DateTimeOffset? due, DateTimeOffset? completed = null)
    {
        var mediator = new Mock<IMediator>();
        mediator.Setup(m => m.Send(It.IsAny<GetJobByIdQuery>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new JobDetailResponseModel(
                1, "JOB-1", "Test", null, 1, "Production",
                1, "Cut", "#94a3b8", null, null, null, null,
                "Normal", null, null, due, null, completed, false, 1, 0, null,
                null, null, null, null, null, null, null, null, null, null, 0,
                Now, Now));
        mediator.Setup(m => m.Send(It.IsAny<GetJobActivityQuery>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);
        var clock = new Mock<IClock>();
        clock.Setup(c => c.UtcNow).Returns(Now);

        var handler = new GetJobStatusHandler(TestDbContextFactory.Create(), mediator.Object, clock.Object);
        var status = await handler.Handle(new GetJobStatusQuery(1), CancellationToken.None);
        return status.IsOverdue;
    }

    [Fact]
    public async Task A_job_due_today_is_not_overdue_yet()
    {
        (await IsOverdueAsync(new DateTimeOffset(2026, 10, 7, 0, 0, 0, TimeSpan.Zero))).Should().BeFalse();
    }

    [Fact]
    public async Task A_job_due_yesterday_is_overdue()
    {
        (await IsOverdueAsync(new DateTimeOffset(2026, 10, 6, 0, 0, 0, TimeSpan.Zero))).Should().BeTrue();
    }

    [Fact]
    public async Task A_completed_job_is_never_overdue()
    {
        (await IsOverdueAsync(new DateTimeOffset(2026, 10, 1, 0, 0, 0, TimeSpan.Zero), Now)).Should().BeFalse();
    }
}
