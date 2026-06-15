using FluentAssertions;

using Forge.Api.Features.Admin;
using Forge.Core.Entities;
using Forge.Tests.Helpers;

namespace Forge.Tests.Handlers.Admin;

/// <summary>
/// Regression for the storage-usage 500: the handler's GroupBy projected into a
/// constructor and then ordered by the projected property, which EF Core could
/// not translate to SQL (it threw InvalidOperationException → 500 on Postgres;
/// the api-smoke E2E caught it). Must run against REAL Postgres — the InMemory
/// provider client-evaluates LINQ and would pass even with the broken query.
/// </summary>
[Collection(PostgresCollection.Name)]
public sealed class GetStorageUsageHandlerTests(PostgresFixture fixture)
{
    [Fact]
    public async Task Handle_GroupsCountsAndOrders_WithoutQueryTranslationError()
    {
        // Unique entity types so assertions are isolated from other collection tests.
        const string big = "StorageUsageTest-Big";
        const string small = "StorageUsageTest-Small";

        await using (var seed = fixture.CreateContext())
        {
            seed.FileAttachments.AddRange(
                Attachment(big, 1000),
                Attachment(big, 2000),
                Attachment(small, 5));
            await seed.SaveChangesAsync();
        }

        await using var db = fixture.CreateContext();

        // Before the fix this threw; the test passing IS the regression guard.
        var result = await new GetStorageUsageHandler(db).Handle(new GetStorageUsageQuery(), default);

        var bigRow = result.Single(r => r.EntityType == big);
        bigRow.FileCount.Should().Be(2);
        bigRow.TotalSizeBytes.Should().Be(3000);

        result.Single(r => r.EntityType == small).TotalSizeBytes.Should().Be(5);

        // Ordered by total size descending across all groups.
        result.Select(r => r.TotalSizeBytes).Should().BeInDescendingOrder();
    }

    private static FileAttachment Attachment(string entityType, long size) => new()
    {
        EntityType = entityType,
        Size = size,
        EntityId = 1,
        UploadedById = 1,
        FileName = "f.bin",
        ContentType = "application/octet-stream",
        BucketName = "test-bucket",
        ObjectKey = "k",
    };
}
