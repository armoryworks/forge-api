using Forge.Api.Features.Deliverables;
using Forge.Core.Entities;
using Forge.Core.Interfaces;
using Forge.Core.Models;
using Forge.Data.Context;
using Forge.Tests.Helpers;

namespace Forge.Tests.Features;

/// <summary>
/// Pro Services rollout — handler-level tests for the Deliverable CRUD
/// surface. Bypasses the HTTP layer (route + auth + capability gate),
/// drives handlers directly against an in-memory DbContext.
/// </summary>
public class DeliverableHandlersTests
{
    /// <summary>
    /// Test-only no-op folder auto-creator. Returns null without touching
    /// any cloud service. Used by Create tests so we don't need to wire
    /// the full cloud-storage stack to exercise CRUD semantics.
    /// </summary>
    private sealed class NoOpFolderAutoCreator : ICloudFolderAutoCreator
    {
        public Task<EntityCloudLink?> AutoCreateAsync(
            string entityType, int entityId,
            IReadOnlyDictionary<string, string> tokenContext,
            CancellationToken ct) => Task.FromResult<EntityCloudLink?>(null);
    }

    private static CreateDeliverableHandler NewCreate(AppDbContext db) =>
        new(db, new NoOpFolderAutoCreator());
    [Fact]
    public async Task Create_Persists_Deliverable_In_Draft_Status()
    {
        using var db = TestDbContextFactory.Create();
        var handler = NewCreate(db);
        var result = await handler.Handle(new CreateDeliverableCommand(
            new CreateDeliverableRequestModel(
                Name: "Discovery Report",
                Description: "Phase 1 findings",
                JobId: 1,
                ProjectId: null,
                CustomerId: 42,
                DeliverableTypeId: 1,
                DueDate: DateTimeOffset.UtcNow.AddDays(7),
                FileAttachmentIds: null,
                CloudLinkExternalId: null)),
            CancellationToken.None);

        Assert.True(result.Id > 0);
        Assert.Equal("Draft", result.Status);
        Assert.Equal("Discovery Report", result.Name);
        Assert.Null(result.DeliveredAt);
        Assert.Null(result.DeliveredByUserId);
    }

    [Fact]
    public async Task Update_To_Delivered_Stamps_DeliveredAt_And_DeliveredByUserId()
    {
        using var db = TestDbContextFactory.Create();
        db.CurrentUserId = 17;
        var created = await NewCreate(db).Handle(
            new CreateDeliverableCommand(new CreateDeliverableRequestModel(
                "Final Deliverable", null, JobId: 5, null, null,
                DeliverableTypeId: 1, null, null, null)),
            CancellationToken.None);

        var updateResult = await new UpdateDeliverableHandler(db).Handle(
            new UpdateDeliverableCommand(created.Id, new UpdateDeliverableRequestModel(
                Name: "Final Deliverable",
                Description: null,
                JobId: 5,
                ProjectId: null,
                CustomerId: null,
                DeliverableTypeId: 1,
                Status: "Delivered",
                DueDate: null,
                FileAttachmentIds: null,
                CloudLinkExternalId: null)),
            CancellationToken.None);

        Assert.Equal("Delivered", updateResult.Status);
        Assert.NotNull(updateResult.DeliveredAt);
        Assert.Equal(17, updateResult.DeliveredByUserId);
    }

    [Fact]
    public async Task Update_Reverting_From_Delivered_Clears_Audit()
    {
        using var db = TestDbContextFactory.Create();
        db.CurrentUserId = 17;
        var created = await NewCreate(db).Handle(
            new CreateDeliverableCommand(new CreateDeliverableRequestModel(
                "Doc", null, 1, null, null, 1, null, null, null)),
            CancellationToken.None);

        // First mark Delivered, then revert to In Review.
        await new UpdateDeliverableHandler(db).Handle(
            new UpdateDeliverableCommand(created.Id, new UpdateDeliverableRequestModel(
                "Doc", null, 1, null, null, 1, "Delivered", null, null, null)),
            CancellationToken.None);
        var reverted = await new UpdateDeliverableHandler(db).Handle(
            new UpdateDeliverableCommand(created.Id, new UpdateDeliverableRequestModel(
                "Doc", null, 1, null, null, 1, "In Review", null, null, null)),
            CancellationToken.None);

        Assert.Equal("In Review", reverted.Status);
        Assert.Null(reverted.DeliveredAt);
        Assert.Null(reverted.DeliveredByUserId);
    }

    [Fact]
    public async Task Delete_Soft_Deletes_The_Row()
    {
        using var db = TestDbContextFactory.Create();
        var created = await NewCreate(db).Handle(
            new CreateDeliverableCommand(new CreateDeliverableRequestModel(
                "Doc", null, 1, null, null, 1, null, null, null)),
            CancellationToken.None);

        await new DeleteDeliverableHandler(db).Handle(
            new DeleteDeliverableCommand(created.Id), CancellationToken.None);

        var rows = await new GetDeliverablesHandler(db).Handle(
            new GetDeliverablesQuery(null, null, null, null), CancellationToken.None);
        // Soft-deleted rows are filtered out by the global query filter.
        Assert.Empty(rows.Items);
    }

    [Fact]
    public async Task List_Filters_By_JobId()
    {
        using var db = TestDbContextFactory.Create();
        var createHandler = NewCreate(db);
        await createHandler.Handle(new CreateDeliverableCommand(new CreateDeliverableRequestModel(
            "Job-1 Deliverable", null, 1, null, null, 1, null, null, null)), CancellationToken.None);
        await createHandler.Handle(new CreateDeliverableCommand(new CreateDeliverableRequestModel(
            "Job-2 Deliverable", null, 2, null, null, 1, null, null, null)), CancellationToken.None);

        var result = await new GetDeliverablesHandler(db).Handle(
            new GetDeliverablesQuery(JobId: 1, null, null, null), CancellationToken.None);

        Assert.Single(result.Items);
        Assert.Equal("Job-1 Deliverable", result.Items[0].Name);
    }

    [Fact]
    public async Task Update_Validator_Rejects_Unknown_Status()
    {
        using var db = TestDbContextFactory.Create();
        var validator = new UpdateDeliverableValidator(db);
        var result = await validator.ValidateAsync(new UpdateDeliverableCommand(1, new UpdateDeliverableRequestModel(
            "Doc", null, 1, null, null, 1, "Bogus", null, null, null)));
        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName.Contains("Status"));
    }

    private static async Task<int> SeedDeliverableTypeAsync(AppDbContext db, string groupCode = "deliverable_type", bool isActive = true)
    {
        var type = new ReferenceData { GroupCode = groupCode, Code = "report", Label = "Report", IsActive = isActive };
        db.ReferenceData.Add(type);
        await db.SaveChangesAsync();
        return type.Id;
    }

    private static CreateDeliverableCommand CreateWithType(int typeId) =>
        new(new CreateDeliverableRequestModel("FAI report", null, 4, null, null, typeId, null, null, null));

    [Fact]
    public async Task Create_Validator_Accepts_A_Seeded_Deliverable_Type()
    {
        using var db = TestDbContextFactory.Create();
        var typeId = await SeedDeliverableTypeAsync(db);

        var result = await new CreateDeliverableValidator(db).ValidateAsync(CreateWithType(typeId));

        Assert.True(result.IsValid);
    }

    [Fact]
    public async Task Create_Validator_Rejects_An_Unknown_Deliverable_Type()
    {
        using var db = TestDbContextFactory.Create();
        await SeedDeliverableTypeAsync(db);

        var result = await new CreateDeliverableValidator(db).ValidateAsync(CreateWithType(999_999));

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName.Contains("DeliverableTypeId"));
    }

    [Fact]
    public async Task Create_Validator_Rejects_A_Reference_Value_From_Another_Group()
    {
        using var db = TestDbContextFactory.Create();
        var otherId = await SeedDeliverableTypeAsync(db, groupCode: "job_priority");

        var result = await new CreateDeliverableValidator(db).ValidateAsync(CreateWithType(otherId));

        Assert.False(result.IsValid);
    }

    [Fact]
    public async Task Update_Validator_Rejects_An_Inactive_Deliverable_Type()
    {
        using var db = TestDbContextFactory.Create();
        var inactiveId = await SeedDeliverableTypeAsync(db, isActive: false);

        var result = await new UpdateDeliverableValidator(db).ValidateAsync(new UpdateDeliverableCommand(1, new UpdateDeliverableRequestModel(
            "Doc", null, 1, null, null, inactiveId, "Draft", null, null, null)));

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName.Contains("DeliverableTypeId"));
    }
}
