using System.Reflection;

using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Abstractions;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Moq;

using Forge.Api.Concurrency;
using Forge.Api.Controllers;
using Forge.Api.Features.Invoices;
using Forge.Core.Entities;
using Forge.Core.Enums;
using Forge.Core.Interfaces;
using Forge.Data.Context;
using Forge.Tests.Helpers;

namespace Forge.Tests.Concurrency;

/// <summary>
/// The invoice number rename and the draft delete are guarded by If-Match: a stale version is refused
/// with 412, the current one goes through, and the detail read hands the UI the version to send.
/// </summary>
public class InvoiceIfMatchTests
{
    [Theory]
    [InlineData(nameof(InvoicesController.RenameInvoiceNumber))]
    [InlineData(nameof(InvoicesController.DeleteInvoice))]
    public void InvoiceMutation_CarriesIfMatch(string action)
    {
        var attribute = typeof(InvoicesController).GetMethod(action)!.GetCustomAttribute<IfMatchAttribute>();

        attribute.Should().NotBeNull();
        attribute!.EntityType.Should().Be(typeof(Invoice));
    }

    [Fact]
    public async Task StaleVersion_Returns412_AndSkipsTheAction()
    {
        using var db = await SeedAsync();
        var invoice = db.Invoices.Single();
        var (context, ranAction) = await RunFilterAsync(db, invoice.Id, $"\"{invoice.Version + 1}\"");

        ranAction.Should().BeFalse();
        context.Result.Should().BeOfType<ObjectResult>()
            .Which.StatusCode.Should().Be(StatusCodes.Status412PreconditionFailed);
    }

    [Fact]
    public async Task CurrentVersion_RunsTheAction()
    {
        using var db = await SeedAsync();
        var invoice = db.Invoices.Single();
        var (context, ranAction) = await RunFilterAsync(db, invoice.Id, $"\"{invoice.Version}\"");

        ranAction.Should().BeTrue();
        context.Result.Should().BeNull();
    }

    [Fact]
    public async Task GetInvoiceById_ReturnsTheVersionAsRowVersion()
    {
        var repo = new Mock<IInvoiceRepository>();
        repo.Setup(r => r.FindWithDetailsAsync(3, It.IsAny<CancellationToken>())).ReturnsAsync(new Invoice
        {
            Id = 3, InvoiceNumber = "INV-3", Version = 7, Customer = new Customer { Name = "Acme" },
        });

        var model = await new GetInvoiceByIdHandler(repo.Object).Handle(new GetInvoiceByIdQuery(3), CancellationToken.None);

        model.RowVersion.Should().Be(7u);
    }

    private static async Task<AppDbContext> SeedAsync()
    {
        var db = TestDbContextFactory.Create();
        var customer = new Customer { Name = "Acme" };
        db.Set<Customer>().Add(customer);
        await db.SaveChangesAsync();
        db.Invoices.Add(new Invoice
        {
            InvoiceNumber = "INV-1", CustomerId = customer.Id, Status = InvoiceStatus.Draft,
            InvoiceDate = DateTimeOffset.UnixEpoch, DueDate = DateTimeOffset.UnixEpoch,
        });
        await db.SaveChangesAsync();
        return db;
    }

    private static async Task<(ActionExecutingContext Context, bool RanAction)> RunFilterAsync(
        AppDbContext db, int id, string ifMatch)
    {
        var services = new ServiceCollection().AddSingleton(db).BuildServiceProvider();
        var http = new DefaultHttpContext { RequestServices = services };
        http.Request.Headers.IfMatch = ifMatch;
        var actionContext = new ActionContext(
            http, new RouteData(new RouteValueDictionary { ["id"] = id.ToString() }), new ActionDescriptor());
        var context = new ActionExecutingContext(
            actionContext, new List<IFilterMetadata>(), new Dictionary<string, object?>(), controller: new object());

        var ran = false;
        await new IfMatchAttribute(typeof(Invoice)).OnActionExecutionAsync(context, () =>
        {
            ran = true;
            return Task.FromResult(new ActionExecutedContext(actionContext, new List<IFilterMetadata>(), new object()));
        });
        return (context, ran);
    }
}
