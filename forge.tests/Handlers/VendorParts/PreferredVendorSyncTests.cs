using System.Text;
using System.Text.Json;

using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Moq;

using Forge.Api.Features.Parts;
using Forge.Api.Features.VendorParts;
using Forge.Api.Workflows;
using Forge.Core.Entities;
using Forge.Core.Enums;
using Forge.Core.Interfaces;
using Forge.Core.Models;
using Forge.Data.Context;
using Forge.Tests.Helpers;

namespace Forge.Tests.Handlers.VendorParts;

/// <summary>
/// Coverage for the two-way sync between <c>Part.PreferredVendorId</c> and the
/// part's <c>VendorPart.IsPreferred</c> flags: part-side writes (UpdatePart, the
/// part workflow) and source-side writes (create, update, import).
/// </summary>
public class PreferredVendorSyncTests
{
    private readonly AppDbContext _db = TestDbContextFactory.Create();

    private sealed record Seeded(Part Part, Vendor VendorA, Vendor VendorB, VendorPart SourceA, VendorPart SourceB);

    private async Task<Seeded> SeedPartPreferringAAsync(bool sourceAPreferred = true, bool sourceBPreferred = false)
    {
        var vendorA = new Vendor { CompanyName = "Vendor A" };
        var vendorB = new Vendor { CompanyName = "Vendor B" };
        _db.Vendors.AddRange(vendorA, vendorB);
        var part = new Part
        {
            PartNumber = "SYNC-001",
            Name = "Bracket",
            ProcurementSource = ProcurementSource.Buy,
            InventoryClass = InventoryClass.Component,
            Status = PartStatus.Active,
        };
        _db.Parts.Add(part);
        await _db.SaveChangesAsync();

        part.PreferredVendorId = vendorA.Id;
        var sourceA = new VendorPart { VendorId = vendorA.Id, PartId = part.Id, IsApproved = true, IsPreferred = sourceAPreferred };
        var sourceB = new VendorPart { VendorId = vendorB.Id, PartId = part.Id, IsApproved = true, IsPreferred = sourceBPreferred };
        _db.VendorParts.AddRange(sourceA, sourceB);
        await _db.SaveChangesAsync();

        return new Seeded(part, vendorA, vendorB, sourceA, sourceB);
    }

    private UpdatePartHandler UpdatePartHandlerFor(Part part)
    {
        var repo = new Mock<IPartRepository>();
        repo.Setup(r => r.FindAsync(part.Id, It.IsAny<CancellationToken>())).ReturnsAsync(part);
        repo.Setup(r => r.SaveChangesAsync(It.IsAny<CancellationToken>()))
            .Returns((CancellationToken ct) => _db.SaveChangesAsync(ct));

        return new UpdatePartHandler(
            repo.Object,
            Mock.Of<ISystemSettingRepository>(),
            Mock.Of<IBarcodeService>(),
            Mock.Of<IBusinessIdentifierService>(),
            Mock.Of<ISyncQueueRepository>(),
            Mock.Of<IAccountingProviderFactory>(),
            _db,
            Mock.Of<ILogger<UpdatePartHandler>>());
    }

    private static UpdatePartRequestModel SetPreferredVendor(int vendorId) =>
        new(null, null, null, null, null, null, null, vendorId, null, null, null, null);

    private static UpdateVendorPartRequestModel SetPreferred(bool isPreferred) =>
        new(null, null, null, null, null, null, null, null, IsApproved: true, IsPreferred: isPreferred, null, null, null);

    private Task<List<ActivityLog>> PreferredVendorLogsAsync() =>
        _db.ActivityLogs.Where(a => a.Action == PreferredVendorSync.ActivityAction).ToListAsync();

    private async Task ReloadAsync(Seeded seeded)
    {
        await _db.Entry(seeded.Part).ReloadAsync();
        await _db.Entry(seeded.SourceA).ReloadAsync();
        await _db.Entry(seeded.SourceB).ReloadAsync();
    }

    [Fact]
    public async Task UpdatePart_NewPreferredVendor_MarksItsSourcePreferred_ClearsTheOthers_AndLogs()
    {
        var seeded = await SeedPartPreferringAAsync();

        await UpdatePartHandlerFor(seeded.Part)
            .Handle(new UpdatePartCommand(seeded.Part.Id, SetPreferredVendor(seeded.VendorB.Id)), CancellationToken.None);
        await ReloadAsync(seeded);

        seeded.Part.PreferredVendorId.Should().Be(seeded.VendorB.Id);
        seeded.SourceA.IsPreferred.Should().BeFalse();
        seeded.SourceB.IsPreferred.Should().BeTrue();

        var logs = await PreferredVendorLogsAsync();
        logs.Select(l => (l.EntityType, l.EntityId)).Should().BeEquivalentTo(new[]
        {
            ("Part", seeded.Part.Id),
            ("Vendor", seeded.VendorA.Id),
            ("Vendor", seeded.VendorB.Id),
        });
        logs.Should().OnlyContain(l => l.Description == "Preferred vendor changed from Vendor A to Vendor B");
    }

    [Fact]
    public async Task UpdatePart_ClearingThePreferredVendor_ClearsEverySource()
    {
        var seeded = await SeedPartPreferringAAsync();

        await UpdatePartHandlerFor(seeded.Part)
            .Handle(new UpdatePartCommand(seeded.Part.Id, SetPreferredVendor(0)), CancellationToken.None);
        await ReloadAsync(seeded);

        seeded.Part.PreferredVendorId.Should().BeNull();
        seeded.SourceA.IsPreferred.Should().BeFalse();
        seeded.SourceB.IsPreferred.Should().BeFalse();
        (await PreferredVendorLogsAsync()).Should().Contain(l => l.EntityType == "Vendor" && l.EntityId == seeded.VendorA.Id);
    }

    [Fact]
    public async Task UpdatePart_ResendingTheSameVendor_LeavesMismatchedSourcesAlone()
    {
        var seeded = await SeedPartPreferringAAsync(sourceAPreferred: false, sourceBPreferred: true);

        await UpdatePartHandlerFor(seeded.Part)
            .Handle(new UpdatePartCommand(seeded.Part.Id, SetPreferredVendor(seeded.VendorA.Id)), CancellationToken.None);
        await ReloadAsync(seeded);

        seeded.Part.PreferredVendorId.Should().Be(seeded.VendorA.Id);
        seeded.SourceA.IsPreferred.Should().BeFalse();
        seeded.SourceB.IsPreferred.Should().BeTrue();
        (await PreferredVendorLogsAsync()).Should().BeEmpty();
    }

    [Fact]
    public async Task PartWorkflow_PreferredVendorPatch_SyncsTheSources()
    {
        var seeded = await SeedPartPreferringAAsync();
        var adapter = new PartWorkflowAdapter(
            _db, Mock.Of<IPartRepository>(), Mock.Of<ISystemSettingRepository>(), Mock.Of<IBusinessIdentifierService>());
        using var patch = JsonDocument.Parse($$"""{"preferredVendorId": {{seeded.VendorB.Id}}}""");

        await adapter.ApplyAsync(seeded.Part.Id, patch.RootElement, CancellationToken.None);
        await ReloadAsync(seeded);

        seeded.Part.PreferredVendorId.Should().Be(seeded.VendorB.Id);
        seeded.SourceA.IsPreferred.Should().BeFalse();
        seeded.SourceB.IsPreferred.Should().BeTrue();
        (await PreferredVendorLogsAsync()).Should().Contain(l => l.EntityType == "Part" && l.EntityId == seeded.Part.Id);
    }

    [Fact]
    public async Task UpdateVendorPart_MakingBPreferred_ChangesThePartsPreferredVendor_AndClearsA()
    {
        var seeded = await SeedPartPreferringAAsync();

        await new UpdateVendorPartHandler(_db)
            .Handle(new UpdateVendorPartCommand(seeded.SourceB.Id, SetPreferred(true)), CancellationToken.None);
        await ReloadAsync(seeded);

        seeded.Part.PreferredVendorId.Should().Be(seeded.VendorB.Id);
        seeded.SourceA.IsPreferred.Should().BeFalse();
        seeded.SourceB.IsPreferred.Should().BeTrue();
        (await PreferredVendorLogsAsync()).Select(l => (l.EntityType, l.EntityId)).Should().BeEquivalentTo(new[]
        {
            ("Part", seeded.Part.Id),
            ("Vendor", seeded.VendorA.Id),
            ("Vendor", seeded.VendorB.Id),
        });
    }

    [Fact]
    public async Task UpdateVendorPart_UnPreferringTheCurrentSource_ClearsThePartsPreferredVendor()
    {
        var seeded = await SeedPartPreferringAAsync();

        await new UpdateVendorPartHandler(_db)
            .Handle(new UpdateVendorPartCommand(seeded.SourceA.Id, SetPreferred(false)), CancellationToken.None);
        await ReloadAsync(seeded);

        seeded.Part.PreferredVendorId.Should().BeNull();
        seeded.SourceA.IsPreferred.Should().BeFalse();
        (await PreferredVendorLogsAsync()).Should().Contain(l => l.EntityType == "Vendor" && l.EntityId == seeded.VendorA.Id);
    }

    [Fact]
    public async Task UpdateVendorPart_UnPreferringASourceThePartDoesNotName_LeavesThePartAlone()
    {
        var seeded = await SeedPartPreferringAAsync(sourceAPreferred: false, sourceBPreferred: true);

        await new UpdateVendorPartHandler(_db)
            .Handle(new UpdateVendorPartCommand(seeded.SourceB.Id, SetPreferred(false)), CancellationToken.None);
        await ReloadAsync(seeded);

        seeded.Part.PreferredVendorId.Should().Be(seeded.VendorA.Id);
        (await PreferredVendorLogsAsync()).Should().BeEmpty();
    }

    [Fact]
    public async Task UpdateVendorPart_EditWithoutAPreferenceChange_DoesNotTouchThePart()
    {
        var seeded = await SeedPartPreferringAAsync(sourceAPreferred: false, sourceBPreferred: true);

        await new UpdateVendorPartHandler(_db)
            .Handle(new UpdateVendorPartCommand(seeded.SourceB.Id, SetPreferred(true) with { LeadTimeDays = 9 }), CancellationToken.None);
        await ReloadAsync(seeded);

        seeded.Part.PreferredVendorId.Should().Be(seeded.VendorA.Id);
        seeded.SourceB.LeadTimeDays.Should().Be(9);
        (await PreferredVendorLogsAsync()).Should().BeEmpty();
    }

    [Fact]
    public async Task CreateVendorPart_Preferred_SetsThePartsPreferredVendor_AndClearsSiblings()
    {
        var seeded = await SeedPartPreferringAAsync();
        var vendorC = new Vendor { CompanyName = "Vendor C" };
        _db.Vendors.Add(vendorC);
        await _db.SaveChangesAsync();

        var created = await new CreateVendorPartHandler(_db).Handle(
            new CreateVendorPartCommand(new CreateVendorPartRequestModel(
                vendorC.Id, seeded.Part.Id, null, null, null, 5, null, null, null, null,
                IsApproved: true, IsPreferred: true, null, null, null)),
            CancellationToken.None);
        await ReloadAsync(seeded);

        seeded.Part.PreferredVendorId.Should().Be(vendorC.Id);
        seeded.SourceA.IsPreferred.Should().BeFalse();
        (await _db.VendorParts.SingleAsync(vp => vp.Id == created.Id)).IsPreferred.Should().BeTrue();
        (await PreferredVendorLogsAsync()).Should().Contain(l => l.EntityType == "Vendor" && l.EntityId == vendorC.Id);
    }

    [Fact]
    public async Task Import_NewSourceForThePartsPreferredVendor_IsPreferred_OnlyWhenNoOtherSourceIs()
    {
        var vendor = new Vendor { CompanyName = "Catalog Vendor" };
        var other = new Vendor { CompanyName = "Other Vendor" };
        _db.Vendors.AddRange(vendor, other);
        await _db.SaveChangesAsync();

        var namesVendor = new Part { PartNumber = "IMP-001", Name = "Names vendor", PreferredVendorId = vendor.Id };
        var namesVendorButOtherFlagged = new Part { PartNumber = "IMP-002", Name = "Other flagged", PreferredVendorId = vendor.Id };
        var namesOther = new Part { PartNumber = "IMP-003", Name = "Names other", PreferredVendorId = other.Id };
        _db.Parts.AddRange(namesVendor, namesVendorButOtherFlagged, namesOther);
        await _db.SaveChangesAsync();
        _db.VendorParts.Add(new VendorPart { VendorId = other.Id, PartId = namesVendorButOtherFlagged.Id, IsPreferred = true });
        await _db.SaveChangesAsync();

        var csv = "partNumber,vendorPartNumber\nIMP-001,C-1\nIMP-002,C-2\nIMP-003,C-3\n";
        var bytes = Encoding.UTF8.GetBytes(csv);
        var file = new FormFile(new MemoryStream(bytes), 0, bytes.Length, "file", "import.csv")
        {
            Headers = new HeaderDictionary(),
            ContentType = "text/csv",
        };

        var result = await new ApplyVendorPartImportHandler(_db)
            .Handle(new ApplyVendorPartImportCommand(vendor.Id, file), CancellationToken.None);

        result.AddedCount.Should().Be(3);
        var imported = await _db.VendorParts
            .Where(vp => vp.VendorId == vendor.Id)
            .ToDictionaryAsync(vp => vp.PartId, vp => vp.IsPreferred);
        imported[namesVendor.Id].Should().BeTrue();
        imported[namesVendorButOtherFlagged.Id].Should().BeFalse();
        imported[namesOther.Id].Should().BeFalse();
        (await _db.Parts.SingleAsync(p => p.Id == namesOther.Id)).PreferredVendorId.Should().Be(other.Id);
    }
}
