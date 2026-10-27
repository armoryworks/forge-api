using System.Net;
using System.Net.Http.Json;
using System.Security.Claims;

using FluentAssertions;
using MediatR;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Moq;

using Forge.Api.Capabilities;
using Forge.Api.Controllers;
using Forge.Api.Features.Imports;
using Forge.Core.Models;
using Forge.Data.Context;
using Forge.Tests.Capabilities;

namespace Forge.Tests.Handlers.Imports;

[Collection(CapabilityTestCollection.Name)]
public class ImportEndpointRoleTests(CapabilityTestWebApplicationFactory factory)
{
    private static readonly object EmptyRows = new { rows = Array.Empty<object>() };

    private HttpClient Client(string role)
    {
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Test-User", "1");
        client.DefaultRequestHeaders.Add("X-Test-Role", role);
        return client;
    }

    [Theory]
    [InlineData("Admin", HttpStatusCode.OK)]
    [InlineData("Manager", HttpStatusCode.OK)]
    [InlineData("OfficeManager", HttpStatusCode.Forbidden)]
    [InlineData("ProductionWorker", HttpStatusCode.Forbidden)]
    public async Task Job_import_is_for_admins_and_managers(string role, HttpStatusCode expected)
    {
        var response = await Client(role).PostAsJsonAsync("/api/v1/imports/jobs/validate", EmptyRows);

        response.StatusCode.Should().Be(expected);
    }

    [Theory]
    [InlineData("OfficeManager", HttpStatusCode.OK)]
    [InlineData("Admin", HttpStatusCode.OK)]
    [InlineData("PM", HttpStatusCode.Forbidden)]
    [InlineData("Engineer", HttpStatusCode.Forbidden)]
    public async Task Invoice_import_is_for_the_billing_roles(string role, HttpStatusCode expected)
    {
        var response = await Client(role).PostAsJsonAsync("/api/v1/imports/invoices/validate", EmptyRows);

        response.StatusCode.Should().Be(expected);
    }

    [Fact]
    public async Task Imports_are_hidden_when_file_import_is_off()
    {
        var admin = Client("Admin");
        bool wasEnabled;
        using (var scope = factory.Services.CreateScope())
            wasEnabled = (await scope.ServiceProvider.GetRequiredService<AppDbContext>().Capabilities.AsNoTracking()
                .SingleAsync(c => c.Code == "CAP-CROSS-INTEG-FILE")).Enabled;
        await admin.PutAsync("/api/v1/capabilities/CAP-CROSS-INTEG-FILE/enabled", JsonContent.Create(new { enabled = false }));
        try
        {
            var jobs = await admin.PostAsJsonAsync("/api/v1/imports/jobs/validate", EmptyRows);
            var invoices = await admin.PostAsJsonAsync("/api/v1/imports/invoices/validate", EmptyRows);

            jobs.StatusCode.Should().Be(HttpStatusCode.Forbidden);
            jobs.Headers.GetValues("X-Capability-Disabled").Should().Contain("CAP-CROSS-INTEG-FILE");
            invoices.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        }
        finally
        {
            if (wasEnabled)
                await admin.PutAsync("/api/v1/capabilities/CAP-CROSS-INTEG-FILE/enabled", JsonContent.Create(new { enabled = true }));
        }
    }

    private static InvoiceImportsController InvoiceControllerFor(string role, bool fullGl, Mock<IMediator> mediator)
    {
        var capabilities = new Mock<ICapabilitySnapshotProvider>();
        capabilities.Setup(c => c.IsEnabled("CAP-ACCT-FULLGL")).Returns(fullGl);
        return new InvoiceImportsController(mediator.Object, capabilities.Object)
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext
                {
                    User = new ClaimsPrincipal(new ClaimsIdentity(
                        [new Claim(ClaimTypes.NameIdentifier, "1"), new Claim(ClaimTypes.Role, role)], "Test")),
                },
            },
        };
    }

    [Fact]
    public async Task Controller_imports_invoices_only_under_full_gl()
    {
        var mediator = new Mock<IMediator>();
        mediator.Setup(m => m.Send(It.IsAny<ImportHistoricalInvoicesCommand>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new DataImportResultModel(0, 0, 0, 0, false, []));
        var request = new InvoiceImportRequestModel([]);

        var withoutGl = await InvoiceControllerFor("Controller", false, mediator).Validate(request, CancellationToken.None);
        var withGl = await InvoiceControllerFor("Controller", true, mediator).Validate(request, CancellationToken.None);

        withoutGl.Result.Should().BeOfType<ForbidResult>();
        withGl.Result.Should().BeOfType<OkObjectResult>();
    }
}
