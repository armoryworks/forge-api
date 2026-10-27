using System.Security.Claims;

using FluentAssertions;
using MediatR;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Moq;

using Forge.Api.Capabilities;
using Forge.Api.Controllers;
using Forge.Api.Features.Search;
using Forge.Core.Models;

namespace Forge.Tests.Handlers.Search;

public class SearchContactGateTests
{
    private readonly Mock<IMediator> _mediator = new();
    private readonly Mock<ICapabilitySnapshotProvider> _capabilities = new();

    public SearchContactGateTests()
    {
        _mediator.Setup(m => m.Send(It.IsAny<GlobalSearchQuery>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<SearchResultModel>());
        _capabilities.Setup(c => c.IsEnabled(It.IsAny<string>())).Returns(true);
    }

    private SearchController ControllerFor(string role) => new(_mediator.Object, _capabilities.Object)
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

    [Theory]
    [InlineData("Admin")]
    [InlineData("PM")]
    [InlineData("OfficeManager")]
    public async Task Customer_roles_search_contacts(string role)
    {
        await ControllerFor(role).Search("avery");

        _mediator.Verify(m => m.Send(It.Is<GlobalSearchQuery>(q => q.IncludeContacts), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Theory]
    [InlineData("ProductionWorker")]
    [InlineData("Procurement")]
    public async Task Roles_without_customer_access_do_not_see_contacts(string role)
    {
        await ControllerFor(role).Search("avery");

        _mediator.Verify(m => m.Send(It.Is<GlobalSearchQuery>(q => !q.IncludeContacts), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Contacts_stay_out_when_the_contacts_capability_is_off()
    {
        _capabilities.Setup(c => c.IsEnabled("CAP-MD-CUSTOMER-CONTACTS")).Returns(false);

        await ControllerFor("Admin").Search("avery");

        _mediator.Verify(m => m.Send(It.Is<GlobalSearchQuery>(q => !q.IncludeContacts), It.IsAny<CancellationToken>()), Times.Once);
    }
}
