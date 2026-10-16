using System.Reflection;

using FluentAssertions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

using Forge.Api.Controllers;

namespace Forge.Tests.Handlers.Terminology;

public class PortalTerminologyEndpointTests
{
    [Fact]
    public void PortalTerminology_IsAnonymousGetUnderThePortalRoute()
    {
        var method = typeof(CustomerPortalController).GetMethod(nameof(CustomerPortalController.Terminology))!;

        method.GetCustomAttribute<HttpGetAttribute>()!.Template.Should().Be("terminology");
        method.GetCustomAttribute<AllowAnonymousAttribute>().Should().NotBeNull();
        typeof(CustomerPortalController).GetCustomAttribute<RouteAttribute>()!.Template.Should().Be("api/v1/portal");
    }
}
