using FluentAssertions;
using Moq;

using Forge.Api.Features.Accounting;
using Forge.Core.Entities;
using Forge.Core.Enums;
using Forge.Integrations;
using Forge.Tests.Helpers;

namespace Forge.Tests.Accounting;

public class CloseJobProductionCostHandlerTests
{
    private readonly Mock<IProductionVariancePostingService> _posting = new();

    public CloseJobProductionCostHandlerTests()
        => _posting
            .Setup(p => p.CloseJobProductionCostAsync(It.IsAny<int>(), It.IsAny<DateOnly>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((int jobId, DateOnly _, int _, CancellationToken _) => new JobProductionCostCloseResult(jobId, 0m, 0m, 0m, true));

    [Fact]
    public async Task OpenJob_IsRefused_AndNothingPosts()
    {
        await using var db = TestDbContextFactory.Create();
        db.Jobs.Add(new Job { Id = 1, JobNumber = "J-1", Title = "Still running" });
        await db.SaveChangesAsync();

        var act = () => new CloseJobProductionCostHandler(db, _posting.Object, new SystemClock())
            .Handle(new CloseJobProductionCostCommand(1), default);

        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("*J-1 is still open*");
        _posting.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task CompletedJob_Closes()
    {
        await using var db = TestDbContextFactory.Create();
        db.Jobs.Add(new Job { Id = 1, JobNumber = "J-1", Title = "Done", CompletedDate = DateTimeOffset.UtcNow });
        await db.SaveChangesAsync();

        var result = await new CloseJobProductionCostHandler(db, _posting.Object, new SystemClock())
            .Handle(new CloseJobProductionCostCommand(1), default);

        result.Posted.Should().BeTrue();
    }

    [Fact]
    public async Task DisposedJob_Closes()
    {
        await using var db = TestDbContextFactory.Create();
        db.Jobs.Add(new Job { Id = 1, JobNumber = "J-1", Title = "Scrapped", Disposition = JobDisposition.Scrap });
        await db.SaveChangesAsync();

        var result = await new CloseJobProductionCostHandler(db, _posting.Object, new SystemClock())
            .Handle(new CloseJobProductionCostCommand(1), default);

        result.Posted.Should().BeTrue();
    }

    [Fact]
    public async Task MissingJob_IsNotFound()
    {
        await using var db = TestDbContextFactory.Create();

        var act = () => new CloseJobProductionCostHandler(db, _posting.Object, new SystemClock())
            .Handle(new CloseJobProductionCostCommand(99), default);

        await act.Should().ThrowAsync<KeyNotFoundException>();
    }
}
