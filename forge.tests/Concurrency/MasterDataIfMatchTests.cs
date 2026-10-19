using System.Reflection;
using System.Text.Json;

using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Abstractions;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

using Forge.Api.Concurrency;
using Forge.Api.Controllers;
using Forge.Api.Features.Customers;
using Forge.Api.Features.EmployeeProfile;
using Forge.Api.Features.Mrp;
using Forge.Api.Features.Vendors;
using Forge.Api.Middleware;
using Forge.Core.Entities;
using Forge.Core.Enums;
using Forge.Core.Interfaces;
using Forge.Core.Models;
using Forge.Data.Context;
using Forge.Tests.Helpers;

namespace Forge.Tests.Concurrency;

/// <summary>
/// The full-form master-data editors (vendor, customer, unit of measure, master schedule, employee
/// profile) are versioned: a save made from a stale form is refused with 412 instead of silently
/// overwriting a concurrent edit, the current version goes through, and the reads hand the UI the
/// version to send.
/// </summary>
public class MasterDataIfMatchTests
{
    [Theory]
    [InlineData(typeof(VendorsController), nameof(VendorsController.UpdateVendor), typeof(Vendor))]
    [InlineData(typeof(VendorsController), nameof(VendorsController.DeleteVendor), typeof(Vendor))]
    [InlineData(typeof(CustomersController), nameof(CustomersController.UpdateCustomer), typeof(Customer))]
    [InlineData(typeof(CustomersController), nameof(CustomersController.DeleteCustomer), typeof(Customer))]
    [InlineData(typeof(InventoryController), nameof(InventoryController.UpdateUnitOfMeasure), typeof(UnitOfMeasure))]
    [InlineData(typeof(MrpController), nameof(MrpController.UpdateMasterSchedule), typeof(MasterSchedule))]
    public void EditorMutation_CarriesIfMatch(Type controller, string action, Type entity)
    {
        var attribute = controller.GetMethod(action)!.GetCustomAttribute<IfMatchAttribute>();

        attribute.Should().NotBeNull();
        attribute!.EntityType.Should().Be(entity);
    }

    [Theory]
    [InlineData(typeof(Vendor))]
    [InlineData(typeof(Customer))]
    [InlineData(typeof(UnitOfMeasure))]
    [InlineData(typeof(MasterSchedule))]
    public async Task StaleVersion_Returns412_AndSkipsTheAction(Type entity)
    {
        using var db = TestDbContextFactory.Create();
        var (id, version) = await SeedAsync(db, entity);

        var (context, ranAction) = await RunFilterAsync(db, entity, id, $"\"{version + 1}\"");

        ranAction.Should().BeFalse();
        context.Result.Should().BeOfType<ObjectResult>()
            .Which.StatusCode.Should().Be(StatusCodes.Status412PreconditionFailed);
    }

    [Theory]
    [InlineData(typeof(Vendor))]
    [InlineData(typeof(Customer))]
    [InlineData(typeof(UnitOfMeasure))]
    [InlineData(typeof(MasterSchedule))]
    public async Task CurrentVersion_RunsTheAction(Type entity)
    {
        using var db = TestDbContextFactory.Create();
        var (id, version) = await SeedAsync(db, entity);

        var (context, ranAction) = await RunFilterAsync(db, entity, id, $"\"{version}\"");

        ranAction.Should().BeTrue();
        context.Result.Should().BeNull();
    }

    [Fact]
    public async Task EditingAVendor_BumpsItsVersion()
    {
        using var db = TestDbContextFactory.Create();
        var vendor = new Vendor { CompanyName = "Acme Supply" };
        db.Vendors.Add(vendor);
        await db.SaveChangesAsync();
        vendor.Version.Should().Be(1u);

        vendor.ContactName = "Pat";
        await db.SaveChangesAsync();

        vendor.Version.Should().Be(2u);
    }

    [Fact]
    public async Task LinesOnlyMasterScheduleEdit_BumpsTheScheduleVersion()
    {
        using var db = TestDbContextFactory.Create();
        var part = new Part { PartNumber = "W-100", Description = "Widget" };
        db.Parts.Add(part);
        var start = new DateTimeOffset(2026, 11, 1, 0, 0, 0, TimeSpan.Zero);
        var schedule = new MasterSchedule
        {
            Name = "Q4", Status = MasterScheduleStatus.Draft, PeriodStart = start, PeriodEnd = start.AddMonths(3),
        };
        schedule.Lines.Add(new MasterScheduleLine { Part = part, Quantity = 10, DueDate = start.AddDays(10) });
        db.MasterSchedules.Add(schedule);
        await db.SaveChangesAsync();
        var line = schedule.Lines.Single();
        var before = schedule.Version;

        var result = await new UpdateMasterScheduleHandler(db).Handle(
            new UpdateMasterScheduleCommand(
                schedule.Id, schedule.Name, schedule.Description, schedule.PeriodStart, schedule.PeriodEnd,
                [new UpdateMasterScheduleLineModel(line.Id, part.Id, 25, line.DueDate, null)]),
            CancellationToken.None);

        result.RowVersion.Should().Be(before + 1);
    }

    [Fact]
    public async Task GetVendorById_ReturnsTheVersionAsRowVersion()
    {
        var repo = new Mock<IVendorRepository>();
        repo.Setup(r => r.FindWithDetailsAsync(4, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Vendor { Id = 4, CompanyName = "Acme Supply", Version = 5 });

        var model = await new GetVendorByIdHandler(repo.Object).Handle(new GetVendorByIdQuery(4), CancellationToken.None);

        model.RowVersion.Should().Be(5u);
    }

    [Fact]
    public async Task GetCustomerById_ReturnsTheVersionAsRowVersion()
    {
        var repo = new Mock<ICustomerRepository>();
        repo.Setup(r => r.FindWithDetailsAsync(6, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Customer { Id = 6, Name = "Acme", Version = 9 });

        var model = await new GetCustomerByIdHandler(repo.Object).Handle(new GetCustomerByIdQuery(6), CancellationToken.None);

        model.RowVersion.Should().Be(9u);
    }

    [Fact]
    public async Task SelfProfileSave_WithAStaleVersion_IsRefusedAndChangesNothing()
    {
        using var db = TestDbContextFactory.Create();
        var profile = await SeedProfileAsync(db);

        var act = () => new UpdateEmployeeProfileHandler(db).Handle(
            new UpdateEmployeeProfileCommand(profile.UserId!.Value, SelfData("Ogden"), $"\"{profile.Version + 1}\""),
            CancellationToken.None);

        await act.Should().ThrowAsync<StaleVersionException>();
        db.ChangeTracker.Clear();
        db.EmployeeProfiles.Single().City.Should().Be("Provo");
    }

    [Fact]
    public async Task SelfProfileSave_WithTheCurrentVersion_SavesAndReturnsTheNextVersion()
    {
        using var db = TestDbContextFactory.Create();
        var profile = await SeedProfileAsync(db);
        var version = profile.Version;

        var result = await new UpdateEmployeeProfileHandler(db).Handle(
            new UpdateEmployeeProfileCommand(profile.UserId!.Value, SelfData("Ogden"), $"\"{version}\""),
            CancellationToken.None);

        result.City.Should().Be("Ogden");
        result.RowVersion.Should().Be(version + 1);
    }

    [Fact]
    public async Task AdminProfileSave_WithAStaleVersion_IsRefused()
    {
        using var db = TestDbContextFactory.Create();
        var profile = await SeedProfileAsync(db);
        var data = new AdminUpdateEmployeeProfileRequestModel(
            null, "Molding", null, null, null, null, null, null, null, null, null, null, null, null);

        var act = () => new AdminUpdateEmployeeProfileHandler(db).Handle(
            new AdminUpdateEmployeeProfileCommand(profile.UserId!.Value, data, $"\"{profile.Version + 1}\""),
            CancellationToken.None);

        await act.Should().ThrowAsync<StaleVersionException>();
    }

    [Fact]
    public async Task ProfileSave_WithoutIfMatch_StaysPermissive()
    {
        using var db = TestDbContextFactory.Create();
        var profile = await SeedProfileAsync(db);

        var result = await new UpdateEmployeeProfileHandler(db).Handle(
            new UpdateEmployeeProfileCommand(profile.UserId!.Value, SelfData("Ogden")), CancellationToken.None);

        result.City.Should().Be("Ogden");
    }

    [Fact]
    public async Task StaleVersionException_MapsToThe412Envelope()
    {
        var middleware = new ExceptionHandlingMiddleware(
            _ => throw new StaleVersionException("\"3\""), NullLogger<ExceptionHandlingMiddleware>.Instance);
        var ctx = new DefaultHttpContext();
        ctx.Response.Body = new MemoryStream();

        await middleware.InvokeAsync(ctx);

        ctx.Response.StatusCode.Should().Be(StatusCodes.Status412PreconditionFailed);
        ctx.Response.Body.Position = 0;
        using var doc = await JsonDocument.ParseAsync(ctx.Response.Body);
        var error = doc.RootElement.GetProperty("errors")[0];
        error.GetProperty("field").GetString().Should().Be("If-Match");
        error.GetProperty("rejectedValue").GetString().Should().Be("\"3\"");
    }

    private static UpdateEmployeeProfileRequestModel SelfData(string city) =>
        new(null, null, null, null, city, null, null, null, null, null, null, null, null);

    private static async Task<EmployeeProfile> SeedProfileAsync(AppDbContext db)
    {
        var profile = new EmployeeProfile { UserId = 42, City = "Provo" };
        db.EmployeeProfiles.Add(profile);
        await db.SaveChangesAsync();
        return profile;
    }

    private static async Task<(int Id, uint Version)> SeedAsync(AppDbContext db, Type entity)
    {
        IConcurrencyVersioned row;
        if (entity == typeof(Vendor))
        {
            var vendor = new Vendor { CompanyName = "Acme Supply" };
            db.Vendors.Add(vendor);
            row = vendor;
        }
        else if (entity == typeof(Customer))
        {
            var customer = new Customer { Name = "Acme" };
            db.Customers.Add(customer);
            row = customer;
        }
        else if (entity == typeof(UnitOfMeasure))
        {
            var uom = new UnitOfMeasure { Code = "ea", Name = "Each", Category = UomCategory.Count };
            db.UnitsOfMeasure.Add(uom);
            row = uom;
        }
        else
        {
            var schedule = new MasterSchedule
            {
                Name = "Q4", Status = MasterScheduleStatus.Draft,
                PeriodStart = DateTimeOffset.UnixEpoch, PeriodEnd = DateTimeOffset.UnixEpoch.AddMonths(3),
            };
            db.MasterSchedules.Add(schedule);
            row = schedule;
        }
        await db.SaveChangesAsync();
        return (((BaseEntity)row).Id, row.Version);
    }

    private static async Task<(ActionExecutingContext Context, bool RanAction)> RunFilterAsync(
        AppDbContext db, Type entity, int id, string ifMatch)
    {
        var services = new ServiceCollection().AddSingleton(db).BuildServiceProvider();
        var http = new DefaultHttpContext { RequestServices = services };
        http.Request.Headers.IfMatch = ifMatch;
        var actionContext = new ActionContext(
            http, new RouteData(new RouteValueDictionary { ["id"] = id.ToString() }), new ActionDescriptor());
        var context = new ActionExecutingContext(
            actionContext, new List<IFilterMetadata>(), new Dictionary<string, object?>(), controller: new object());

        var ran = false;
        await new IfMatchAttribute(entity).OnActionExecutionAsync(context, () =>
        {
            ran = true;
            return Task.FromResult(new ActionExecutedContext(actionContext, new List<IFilterMetadata>(), new object()));
        });
        return (context, ran);
    }
}
