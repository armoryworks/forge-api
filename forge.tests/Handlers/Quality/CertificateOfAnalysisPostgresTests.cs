using System.Text.Json;

using FluentAssertions;
using MediatR;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;

using QuestPDF.Infrastructure;
using UglyToad.PdfPig;

using Forge.Api.Capabilities;
using Forge.Api.Features.DomainEvents;
using Forge.Api.Features.DomainEvents.Handlers;
using Forge.Api.Features.Quality.Coa;
using Forge.Core.Entities;
using Forge.Core.Enums;
using Forge.Core.Interfaces;
using Forge.Core.Models;
using Forge.Data.Context;
using Forge.Integrations;
using Forge.Tests.Helpers;

namespace Forge.Tests.Handlers.Quality;

/// <summary>
/// CAP-QC-COA — a certificate of analysis certifies a lot from its completed inspection results and SPC
/// readings; an out-of-specification result marks the certificate as not conforming and keeps it off a
/// shipment (a warning instead), the PDF prints in Spanish when asked, and a customer that requires a
/// certificate gets one attached when the shipment ships.
/// </summary>
[Collection(PostgresCollection.Name)]
public sealed class CertificateOfAnalysisPostgresTests(PostgresFixture fixture)
{
    private sealed class FixedClock : IClock
    {
        public DateTimeOffset UtcNow { get; } = new(2026, 10, 1, 12, 0, 0, TimeSpan.Zero);
    }

    private sealed record Seeded(int UserId, Part Part, LotRecord Lot, Customer Customer, Shipment Shipment);

    private static CertificateOfAnalysisIssuer Issuer(AppDbContext db, MockStorageService? storage = null) => new(
        db,
        new FixedClock(),
        storage ?? new MockStorageService(NullLogger<MockStorageService>.Instance),
        Options.Create(new MinioOptions()));

    private static async Task<Seeded> SeedAsync(
        AppDbContext db, bool inspectionPasses = true, decimal[]? spcValues = null, bool shipLot = true, bool requiresCoa = false)
    {
        QuestPDF.Settings.License = LicenseType.Community;
        var tag = Guid.NewGuid().ToString("N")[..8];
        var user = new ApplicationUser
        {
            UserName = $"coa{tag}@t.com", Email = $"coa{tag}@t.com",
            FirstName = "Quinn", LastName = "Inspector", Initials = "QI", AvatarColor = "#888",
        };
        var part = new Part { PartNumber = $"COA-{tag}", Name = "Resin pellet", InventoryClass = InventoryClass.FinishedGood };
        var customer = new Customer { Name = $"CoA customer {tag}", RequiresCoa = requiresCoa };
        db.AddRange(user, part, customer);
        await db.SaveChangesAsync();

        var lot = new LotRecord { LotNumber = $"L-{tag}", PartId = part.Id, Quantity = 100m, PartRevision = "B" };
        db.LotRecords.Add(lot);
        await db.SaveChangesAsync();

        var template = new QcChecklistTemplate { Name = $"Final {tag}" };
        template.Items.Add(new QcChecklistItem { Description = "Melt flow index", Specification = "10 ± 0.5", SortOrder = 1 });
        template.Items.Add(new QcChecklistItem { Description = "Colour", Specification = "Natural", SortOrder = 2 });
        db.QcChecklistTemplates.Add(template);
        await db.SaveChangesAsync();

        var inspection = new QcInspection
        {
            PartId = part.Id, TemplateId = template.Id, InspectorId = user.Id, LotNumber = lot.LotNumber,
            Status = inspectionPasses ? "Passed" : "Failed", CompletedAt = new DateTimeOffset(2026, 9, 30, 0, 0, 0, TimeSpan.Zero),
        };
        var items = template.Items.OrderBy(i => i.SortOrder).ToList();
        inspection.Results.Add(new QcInspectionResult
        {
            ChecklistItemId = items[0].Id, Description = items[0].Description, Specification = items[0].Specification,
            MeasuredValue = inspectionPasses ? "10.2" : "11.1", Passed = inspectionPasses,
        });
        inspection.Results.Add(new QcInspectionResult
        {
            ChecklistItemId = items[1].Id, Description = items[1].Description, Specification = items[1].Specification,
            MeasuredValue = "Natural", Passed = true,
        });
        db.QcInspections.Add(inspection);
        await db.SaveChangesAsync();

        if (spcValues is not null)
        {
            var characteristic = new SpcCharacteristic
            {
                PartId = part.Id, Name = "Pellet diameter", NominalValue = 3m, LowerSpecLimit = 2.9m, UpperSpecLimit = 3.1m,
                UnitOfMeasure = "mm", DecimalPlaces = 2,
            };
            db.SpcCharacteristics.Add(characteristic);
            await db.SaveChangesAsync();
            db.SpcMeasurements.Add(new SpcMeasurement
            {
                CharacteristicId = characteristic.Id, LotNumber = lot.LotNumber, MeasuredById = user.Id,
                MeasuredAt = new DateTimeOffset(2026, 9, 29, 0, 0, 0, TimeSpan.Zero), SubgroupNumber = 1,
                ValuesJson = JsonSerializer.Serialize(spcValues), Mean = spcValues.Average(),
                IsOutOfSpec = spcValues.Any(v => v < 2.9m || v > 3.1m),
            });
            await db.SaveChangesAsync();
        }

        var order = new SalesOrder
        {
            OrderNumber = $"SO-{tag}", CustomerId = customer.Id, Status = SalesOrderStatus.Confirmed,
            Lines = [new SalesOrderLine { PartId = part.Id, Description = "Resin pellet", Quantity = 40m, UnitPrice = 2m, LineNumber = 1 }],
        };
        db.SalesOrders.Add(order);
        await db.SaveChangesAsync();
        var shipment = new Shipment
        {
            ShipmentNumber = $"SH-{tag}", SalesOrderId = order.Id, Status = ShipmentStatus.Shipped,
            ShippedDate = new DateTimeOffset(2026, 10, 1, 0, 0, 0, TimeSpan.Zero),
            Lines = [new ShipmentLine { SalesOrderLineId = order.Lines.First().Id, PartId = part.Id, Quantity = 40m }],
        };
        db.Shipments.Add(shipment);
        await db.SaveChangesAsync();

        if (shipLot)
        {
            db.BinMovements.Add(new BinMovement
            {
                EntityType = "part", EntityId = part.Id, Quantity = -40m, LotNumber = lot.LotNumber,
                ShipmentLineId = shipment.Lines.First().Id, Reason = BinMovementReason.Ship, MovedBy = user.Id,
                MovedAt = DateTimeOffset.UtcNow,
            });
            await db.SaveChangesAsync();
        }

        return new Seeded(user.Id, part, lot, customer, shipment);
    }

    private static OnShipmentShipped_AttachCertificates Reaction(AppDbContext db, bool enabled)
    {
        var capabilities = new Mock<ICapabilitySnapshotProvider>();
        capabilities.Setup(c => c.IsEnabled("CAP-QC-COA")).Returns(enabled);
        var attach = new AttachShipmentCertificatesHandler(Issuer(db), new HttpContextAccessor());
        var mediator = new Mock<IMediator>();
        mediator.Setup(m => m.Send(It.IsAny<AttachShipmentCertificatesCommand>(), It.IsAny<CancellationToken>()))
            .Returns((AttachShipmentCertificatesCommand command, CancellationToken ct) => attach.Handle(command, ct));
        return new OnShipmentShipped_AttachCertificates(
            db, capabilities.Object, mediator.Object, NullLogger<OnShipmentShipped_AttachCertificates>.Instance);
    }

    private static string Text(byte[] pdf)
    {
        using var document = PdfDocument.Open(pdf);
        return string.Join(" ", document.GetPages().SelectMany(p => p.GetWords()).Select(w => w.Text));
    }

    [Fact]
    public async Task Generates_from_the_lots_inspection_results_and_spc_readings()
    {
        await using var db = fixture.CreateContext();
        var seed = await SeedAsync(db, spcValues: [2.95m, 3.0m, 3.05m]);

        var certificate = await Issuer(db).IssueAsync(seed.Lot.Id, seed.Customer.Id, null, "en", null, seed.UserId, default);

        certificate.CertificateNumber.Should().StartWith("COA-");
        certificate.Conforms.Should().BeTrue();
        certificate.FileAttachmentId.Should().NotBeNull();
        var results = certificate.Results.OrderBy(r => r.SortOrder).ToList();
        results.Select(r => r.Characteristic).Should().Equal("Melt flow index", "Colour", "Pellet diameter");
        results[0].SpecMin.Should().Be(9.5m);
        results[0].SpecMax.Should().Be(10.5m);
        results[0].SpecTarget.Should().Be(10m);
        results[0].Result.Should().Be("10.2");
        results[2].SourceType.Should().Be(CoaResult.SourceSpc);
        results[2].Result.Should().Be("3.00 (n=3, 2.95 – 3.05)");
        results[2].Unit.Should().Be("mm");
        results.Should().OnlyContain(r => r.Pass);

        var stored = await db.FileAttachments.AsNoTracking().SingleAsync(f => f.Id == certificate.FileAttachmentId);
        stored.EntityType.Should().Be("lots");
        stored.EntityId.Should().Be(seed.Lot.Id);
    }

    [Fact]
    public async Task An_out_of_spec_result_marks_the_certificate_not_conforming()
    {
        await using var db = fixture.CreateContext();
        var seed = await SeedAsync(db, spcValues: [2.95m, 3.2m]);

        var certificate = await Issuer(db).IssueAsync(seed.Lot.Id, null, null, null, null, seed.UserId, default);

        certificate.Conforms.Should().BeFalse();
        certificate.Results.Single(r => r.Characteristic == "Pellet diameter").Pass.Should().BeFalse();
    }

    [Fact]
    public async Task A_lot_with_no_results_cannot_be_certified()
    {
        await using var db = fixture.CreateContext();
        var seed = await SeedAsync(db);
        var other = new LotRecord { LotNumber = $"EMPTY-{Guid.NewGuid():N}"[..20], PartId = seed.Part.Id, Quantity = 5m };
        db.LotRecords.Add(other);
        await db.SaveChangesAsync();

        var act = () => Issuer(db).IssueAsync(other.Id, null, null, null, null, seed.UserId, default);

        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("*no completed inspection or SPC results*");
    }

    [Fact]
    public async Task Attaching_to_a_shipment_certifies_each_shipped_lot_for_the_shipments_customer()
    {
        await using var db = fixture.CreateContext();
        var seed = await SeedAsync(db);

        var result = await Issuer(db).AttachToShipmentAsync(seed.Shipment.Id, null, null, seed.UserId, default);
        var again = await Issuer(db).AttachToShipmentAsync(seed.Shipment.Id, null, null, seed.UserId, default);

        result.Warnings.Should().BeEmpty();
        result.Attached.Should().ContainSingle();
        result.Attached[0].LotNumber.Should().Be(seed.Lot.LotNumber);
        result.Attached[0].CustomerId.Should().Be(seed.Customer.Id);
        result.Attached[0].ShipmentId.Should().Be(seed.Shipment.Id);
        again.Attached.Should().ContainSingle("a lot already certified for the shipment is not certified twice");
    }

    [Fact]
    public async Task An_out_of_spec_lot_is_not_attached_and_comes_back_as_a_warning()
    {
        await using var db = fixture.CreateContext();
        var seed = await SeedAsync(db, inspectionPasses: false);

        var result = await Issuer(db).AttachToShipmentAsync(seed.Shipment.Id, null, null, seed.UserId, default);

        result.Attached.Should().BeEmpty();
        var warning = result.Warnings.Should().ContainSingle().Subject;
        warning.Code.Should().Be(CertificateOfAnalysisIssuer.NonConformingCode);
        warning.FailedCharacteristics.Should().Equal("Melt flow index");
        (await db.CertificatesOfAnalysis.AnyAsync(c => c.LotRecordId == seed.Lot.Id)).Should().BeFalse();
    }

    [Fact]
    public async Task The_pdf_prints_in_spanish_when_asked()
    {
        await using var db = fixture.CreateContext();
        var seed = await SeedAsync(db);
        var certificate = await Issuer(db).IssueAsync(seed.Lot.Id, seed.Customer.Id, null, "en", null, seed.UserId, default);

        var spanish = Text(await Issuer(db).RenderPdfAsync(certificate.Id, "es-US", default));
        var english = Text(await Issuer(db).RenderPdfAsync(certificate.Id, null, default));

        spanish.Should().Contain("CERTIFICADO").And.Contain("ANÁLISIS").And.Contain("Pieza:").And.Contain("Resultado").And.Contain("Cumple");
        spanish.Should().Contain(seed.Lot.LotNumber).And.Contain("Melt");
        english.Should().Contain("CERTIFICATE").And.Contain("Result").And.Contain("Pass").And.NotContain("Cumple");
    }

    [Fact]
    public async Task Shipping_to_a_customer_that_requires_a_certificate_attaches_one()
    {
        await using var db = fixture.CreateContext();
        var seed = await SeedAsync(db, requiresCoa: true);
        var handler = Reaction(db, enabled: true);

        await handler.Handle(new ShipmentShippedEvent(seed.Shipment.Id, seed.Shipment.SalesOrderId, seed.UserId), default);

        (await db.CertificatesOfAnalysis.CountAsync(c => c.ShipmentId == seed.Shipment.Id)).Should().Be(1);
    }

    [Fact]
    public async Task Shipping_an_out_of_spec_lot_logs_why_no_certificate_was_attached()
    {
        await using var db = fixture.CreateContext();
        var seed = await SeedAsync(db, inspectionPasses: false, requiresCoa: true);
        var handler = Reaction(db, enabled: true);

        await handler.Handle(new ShipmentShippedEvent(seed.Shipment.Id, seed.Shipment.SalesOrderId, seed.UserId), default);

        (await db.CertificatesOfAnalysis.AnyAsync(c => c.ShipmentId == seed.Shipment.Id)).Should().BeFalse();
        (await db.ActivityLogs.AnyAsync(a => a.EntityType == "Shipment" && a.EntityId == seed.Shipment.Id
            && a.Action == "coa-not-attached")).Should().BeTrue();
    }

    [Fact]
    public async Task Nothing_is_attached_when_the_customer_does_not_require_a_certificate_or_the_capability_is_off()
    {
        await using var db = fixture.CreateContext();
        var notRequired = await SeedAsync(db);
        var required = await SeedAsync(db, requiresCoa: true);
        await Reaction(db, enabled: true)
            .Handle(new ShipmentShippedEvent(notRequired.Shipment.Id, notRequired.Shipment.SalesOrderId, notRequired.UserId), default);
        await Reaction(db, enabled: false)
            .Handle(new ShipmentShippedEvent(required.Shipment.Id, required.Shipment.SalesOrderId, required.UserId), default);

        (await db.CertificatesOfAnalysis.AnyAsync(c => c.ShipmentId == notRequired.Shipment.Id || c.ShipmentId == required.Shipment.Id))
            .Should().BeFalse();
    }
}
