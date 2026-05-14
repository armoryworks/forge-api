using FluentAssertions;
using Forge.Core.Entities;
using Forge.Tests.Helpers;

namespace Forge.Tests.Persistence;

/// <summary>
/// Verifies the data tier auto-stamps DeletedBy from CurrentUserId
/// whenever a soft delete (DeletedAt assignment) is committed without
/// the handler also setting DeletedBy. Centralizing the audit stamp
/// in AppDbContext means every soft-delete site gets it for free.
/// </summary>
public class SoftDeleteAutoStampTests
{
    [Fact]
    public async Task SoftDelete_AutoStampsDeletedBy_FromCurrentUserId()
    {
        await using var db = TestDbContextFactory.Create();
        db.CurrentUserId = 42;

        var part = new Part
        {
            PartNumber = "PRT-00001",
            Name = "Test Part",
            Revision = "A",
            ProcurementSource = ProcurementSource.Buy,
            InventoryClass = InventoryClass.Component,
            Status = PartStatus.Active,
            TraceabilityType = TraceabilityType.None,
        };
        db.Parts.Add(part);
        await db.SaveChangesAsync();

        // Handler-style soft delete: stamp DeletedAt only.
        part.DeletedAt = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync();

        part.DeletedBy.Should().Be("42",
            "AppDbContext.SetTimestamps must auto-stamp DeletedBy from CurrentUserId");
    }

    [Fact]
    public async Task SoftDelete_DoesNotOverwriteExplicitDeletedBy()
    {
        await using var db = TestDbContextFactory.Create();
        db.CurrentUserId = 42;

        var part = new Part
        {
            PartNumber = "PRT-00002",
            Name = "Test Part 2",
            Revision = "A",
            ProcurementSource = ProcurementSource.Buy,
            InventoryClass = InventoryClass.Component,
            Status = PartStatus.Active,
            TraceabilityType = TraceabilityType.None,
        };
        db.Parts.Add(part);
        await db.SaveChangesAsync();

        // Handler explicitly stamps DeletedBy with a different value
        // (e.g., system user, admin acting on behalf of, etc.) — auto-stamp
        // must respect that.
        part.DeletedAt = DateTimeOffset.UtcNow;
        part.DeletedBy = "system";
        await db.SaveChangesAsync();

        part.DeletedBy.Should().Be("system",
            "explicitly-set DeletedBy values must not be overwritten by the auto-stamp");
    }

    [Fact]
    public async Task SoftDelete_WithNullCurrentUserId_LeavesDeletedByNull()
    {
        await using var db = TestDbContextFactory.Create();
        db.CurrentUserId = null; // System-initiated (Hangfire job, seed, etc.)

        var part = new Part
        {
            PartNumber = "PRT-00003",
            Name = "Test Part 3",
            Revision = "A",
            ProcurementSource = ProcurementSource.Buy,
            InventoryClass = InventoryClass.Component,
            Status = PartStatus.Active,
            TraceabilityType = TraceabilityType.None,
        };
        db.Parts.Add(part);
        await db.SaveChangesAsync();

        part.DeletedAt = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync();

        part.DeletedBy.Should().BeNull(
            "no current user means no audit principal — leave DeletedBy null rather than fabricate one");
    }

    [Fact]
    public async Task NormalUpdate_DoesNotTouchDeletedBy()
    {
        await using var db = TestDbContextFactory.Create();
        db.CurrentUserId = 42;

        var part = new Part
        {
            PartNumber = "PRT-00004",
            Name = "Test Part 4",
            Revision = "A",
            ProcurementSource = ProcurementSource.Buy,
            InventoryClass = InventoryClass.Component,
            Status = PartStatus.Active,
            TraceabilityType = TraceabilityType.None,
        };
        db.Parts.Add(part);
        await db.SaveChangesAsync();

        // Non-delete update — DeletedBy stays null because DeletedAt is also null.
        part.Name = "Renamed";
        await db.SaveChangesAsync();

        part.DeletedAt.Should().BeNull();
        part.DeletedBy.Should().BeNull(
            "auto-stamp must only fire on soft deletes, not on routine updates");
    }
}
