using FluentAssertions;

using Forge.Api.Features.Jobs.Operations;
using Forge.Core.Enums;
using Forge.Core.Models;
using Forge.Tests.Helpers;

namespace Forge.Tests.Handlers.Jobs.Operations;

public class GetJobOperationEventsHandlerTests
{
    private readonly JobOperationTestHarness _h = new();

    private Task<IReadOnlyList<JobOperationEventResponseModel>> GetAsync(int jobId, int operationId)
        => new GetJobOperationEventsHandler(_h.Timers.Db)
            .Handle(new GetJobOperationEventsQuery(jobId, operationId), CancellationToken.None);

    [Fact]
    public async Task Handle_ListsTheStepsEventsInOrderWithWhoReportedThem()
    {
        var (job, routing) = await _h.AddJobWithRoutingAsync(40m);
        var user = await _h.Timers.AddUserAsync();
        var start = _h.Timers.Now;
        await _h.ProgressHandler(user.Id).Handle(
            new UpdateJobOperationProgressCommand(job.Id, routing[0].Id, new(5m, null, null)), CancellationToken.None);
        _h.Timers.Now = start.AddMinutes(10);
        await _h.ProgressHandler(user.Id).Handle(
            new UpdateJobOperationProgressCommand(job.Id, routing[0].Id, new(null, 1m, null, ReworkQuantity: 2m, ReasonCode: "CHATTER")),
            CancellationToken.None);
        await _h.ProgressHandler(user.Id).Handle(
            new UpdateJobOperationProgressCommand(job.Id, routing[1].Id, new(7m, null, null)), CancellationToken.None);

        var result = await GetAsync(job.Id, routing[0].Id);

        result.Select(e => (e.Kind, e.Quantity, e.ReasonCode)).Should().Equal(
            (JobOperationEventKind.Good, 5m, (string?)null),
            (JobOperationEventKind.Scrap, 1m, "CHATTER"),
            (JobOperationEventKind.Rework, 2m, "CHATTER"));
        result[0].OccurredAt.Should().Be(start);
        result[2].OccurredAt.Should().Be(start.AddMinutes(10));
        result.Should().OnlyContain(e => e.UserId == user.Id && e.UserName == "Machinist, Pat");
    }

    [Fact]
    public async Task Handle_UntouchedStep_IsEmpty()
    {
        var (job, routing) = await _h.AddJobWithRoutingAsync(40m);

        (await GetAsync(job.Id, routing[2].Id)).Should().BeEmpty();
    }

    [Fact]
    public async Task Handle_UnknownJob_IsNotFound()
    {
        var act = () => GetAsync(9999, 1);

        await act.Should().ThrowAsync<KeyNotFoundException>();
    }
}
