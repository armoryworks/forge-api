using FluentAssertions;

using Forge.Api.Features.Jobs.Operations;
using Forge.Core.Entities;
using Forge.Tests.Helpers;

namespace Forge.Tests.Handlers.Jobs.Operations;

public class GetJobOperationsConfigHandlerTests
{
    private readonly JobOperationTestHarness _h = new();

    private Task<Forge.Core.Models.JobOperationsConfigResponseModel> GetAsync()
        => new GetJobOperationsConfigHandler(_h.Operations, _h.Timers.Db)
            .Handle(new GetJobOperationsConfigQuery(), CancellationToken.None);

    [Fact]
    public async Task Handle_ListsActiveReasonsInOrder()
    {
        _h.Timers.Db.ReferenceData.AddRange(
            new ReferenceData { GroupCode = GetJobOperationsConfigHandler.ReasonGroupCode, Code = "tooling", Label = "Tool wear", SortOrder = 2 },
            new ReferenceData { GroupCode = GetJobOperationsConfigHandler.ReasonGroupCode, Code = "dimension", Label = "Out of tolerance", SortOrder = 1 },
            new ReferenceData { GroupCode = GetJobOperationsConfigHandler.ReasonGroupCode, Code = "retired", Label = "Retired", SortOrder = 0, IsActive = false },
            new ReferenceData { GroupCode = "return_reason", Code = "defective", Label = "Defective", SortOrder = 1 });
        await _h.Timers.Db.SaveChangesAsync();

        var result = await GetAsync();

        result.OperationTracking.Should().BeTrue();
        result.Reasons.Select(r => (r.Code, r.Label)).Should().Equal(
            ("dimension", "Out of tolerance"),
            ("tooling", "Tool wear"));
    }

    [Fact]
    public async Task Handle_NoReasonsConfigured_IsEmptyNotNull()
    {
        _h.TrackingEnabled = false;

        var result = await GetAsync();

        result.OperationTracking.Should().BeFalse();
        result.Reasons.Should().BeEmpty();
    }
}
