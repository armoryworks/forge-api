using Moq;

using Forge.Api.Features.Jobs;
using Forge.Core.Interfaces;
using Forge.Core.Models;
using Forge.Tests.Helpers;

namespace Forge.Tests.Handlers.Jobs;

public class GetJobsTeamFilterTests
{
    private readonly JobOperationTestHarness _h = new();
    private readonly Mock<IJobRepository> _repo = new();

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task TeamFilter_ReadsTheCurrentOperationTheWayTheTrackingSettingSays(bool tracking)
    {
        _h.TrackingEnabled = tracking;
        var query = new JobListQuery { TrackTypeId = 3, TeamId = 7 };
        _repo.Setup(r => r.GetPagedJobsAsync(query, tracking, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PagedResponse<JobListResponseModel>([], 0, 1, 25));

        await new GetJobsHandler(_repo.Object, _h.Operations)
            .Handle(new GetJobsQuery(query), CancellationToken.None);

        _repo.Verify(r => r.GetPagedJobsAsync(query, tracking, It.IsAny<CancellationToken>()), Times.Once);
    }
}
