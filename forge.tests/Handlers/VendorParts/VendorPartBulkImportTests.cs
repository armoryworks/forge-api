using System.Text;

using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;

using Forge.Api.Features.VendorParts;
using Forge.Core.Entities;
using Forge.Core.Models;
using Forge.Data.Context;
using Forge.Tests.Helpers;

namespace Forge.Tests.Handlers.VendorParts;

/// <summary>
/// Coverage for the VendorPart CSV bulk-import preview + apply handlers.
/// Mirrors the price-list-entry importer: dry-run preview classifies rows;
/// apply upserts by (vendorId, partId).
/// </summary>
public class VendorPartBulkImportTests
{
    private readonly AppDbContext _db;
    private readonly int _vendorId;
    private readonly int _partAId;
    private readonly int _partBId;

    public VendorPartBulkImportTests()
    {
        _db = TestDbContextFactory.Create();

        var vendor = new Vendor { CompanyName = "Acme Supply" };
        _db.Vendors.Add(vendor);

        var partA = new Part { PartNumber = "PART-001", Name = "Widget A" };
        var partB = new Part { PartNumber = "PART-002", Name = "Widget B" };
        _db.Parts.Add(partA);
        _db.Parts.Add(partB);
        _db.SaveChanges();

        _vendorId = vendor.Id;
        _partAId = partA.Id;
        _partBId = partB.Id;
    }

    private static IFormFile MakeCsv(string content)
    {
        var bytes = Encoding.UTF8.GetBytes(content);
        var stream = new MemoryStream(bytes);
        return new FormFile(stream, 0, bytes.Length, "file", "import.csv")
        {
            Headers = new HeaderDictionary(),
            ContentType = "text/csv",
        };
    }

    [Fact]
    public async Task Preview_ValidRows_ReturnsAddActionForAll()
    {
        var csv = "partNumber,vendorPartNumber,leadTimeDays\nPART-001,VP-1,7\nPART-002,VP-2,14\n";
        var handler = new PreviewVendorPartImportHandler(_db);

        var result = await handler.Handle(
            new PreviewVendorPartImportCommand(_vendorId, MakeCsv(csv)), CancellationToken.None);

        result.TotalRows.Should().Be(2);
        result.AddCount.Should().Be(2);
        result.ErrorCount.Should().Be(0);
        result.Rows.Should().AllSatisfy(r => r.Action.Should().Be(BulkImportRowAction.Add));
    }

    [Fact]
    public async Task Preview_UnknownPartNumber_ReturnsErrorRow()
    {
        var csv = "partNumber,vendorPartNumber\nPART-NOPE,VP-9\n";
        var handler = new PreviewVendorPartImportHandler(_db);

        var result = await handler.Handle(
            new PreviewVendorPartImportCommand(_vendorId, MakeCsv(csv)), CancellationToken.None);

        result.ErrorCount.Should().Be(1);
        result.Rows[0].Action.Should().Be(BulkImportRowAction.Error);
        result.Rows[0].ErrorMessage.Should().Contain("not found");
    }

    [Fact]
    public async Task Preview_NegativeLeadTime_ReturnsError()
    {
        var csv = "partNumber,leadTimeDays\nPART-001,-3\n";
        var handler = new PreviewVendorPartImportHandler(_db);

        var result = await handler.Handle(
            new PreviewVendorPartImportCommand(_vendorId, MakeCsv(csv)), CancellationToken.None);

        result.ErrorCount.Should().Be(1);
        result.Rows[0].ErrorMessage.Should().Contain("leadTimeDays");
    }

    [Fact]
    public async Task Preview_DuplicatePartInFile_SecondRowErrors()
    {
        var csv = "partNumber,vendorPartNumber\nPART-001,VP-1\nPART-001,VP-1-DUP\n";
        var handler = new PreviewVendorPartImportHandler(_db);

        var result = await handler.Handle(
            new PreviewVendorPartImportCommand(_vendorId, MakeCsv(csv)), CancellationToken.None);

        result.AddCount.Should().Be(1);
        result.ErrorCount.Should().Be(1);
        result.Rows[1].Action.Should().Be(BulkImportRowAction.Error);
        result.Rows[1].ErrorMessage.Should().Contain("more than once");
    }

    [Fact]
    public async Task Apply_AddRows_PersistsVendorParts()
    {
        var csv = "partNumber,vendorPartNumber,leadTimeDays,minOrderQty,notes\nPART-001,VP-1,7,10,primary\nPART-002,VP-2,14,,\n";
        var handler = new ApplyVendorPartImportHandler(_db);

        var result = await handler.Handle(
            new ApplyVendorPartImportCommand(_vendorId, MakeCsv(csv)), CancellationToken.None);

        result.AddedCount.Should().Be(2);
        result.UpdatedCount.Should().Be(0);
        result.ErrorCount.Should().Be(0);

        var saved = await _db.VendorParts.Where(vp => vp.VendorId == _vendorId).ToListAsync();
        saved.Should().HaveCount(2);
        saved.Should().Contain(vp => vp.PartId == _partAId && vp.VendorPartNumber == "VP-1"
            && vp.LeadTimeDays == 7 && vp.MinOrderQty == 10 && vp.Notes == "primary");
        saved.Should().Contain(vp => vp.PartId == _partBId && vp.VendorPartNumber == "VP-2" && vp.LeadTimeDays == 14);
    }

    [Fact]
    public async Task Apply_RerunSameCsv_IsIdempotent()
    {
        var csv = "partNumber,vendorPartNumber\nPART-001,VP-1\nPART-002,VP-2\n";
        var handler = new ApplyVendorPartImportHandler(_db);

        var first = await handler.Handle(
            new ApplyVendorPartImportCommand(_vendorId, MakeCsv(csv)), CancellationToken.None);
        first.AddedCount.Should().Be(2);

        var second = await handler.Handle(
            new ApplyVendorPartImportCommand(_vendorId, MakeCsv(csv)), CancellationToken.None);
        second.AddedCount.Should().Be(0);
        second.UpdatedCount.Should().Be(2);

        var saved = await _db.VendorParts.Where(vp => vp.VendorId == _vendorId).ToListAsync();
        saved.Should().HaveCount(2);
    }
}
