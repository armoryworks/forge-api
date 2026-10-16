using System.Security.Claims;

using FluentAssertions;
using MediatR;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Moq;

using Forge.Api.Controllers;
using Forge.Api.Features.Customers;
using Forge.Core.Models;

namespace Forge.Tests.Handlers.Customers;

public class UpdateCustomerCommercialTermsRoleTests
{
    private readonly Mock<IMediator> _mediator = new();

    private CustomersController ControllerFor(string role) => new(_mediator.Object)
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

    private static UpdateCustomerRequestModel CreditChange() =>
        new(null, null, null, null, null, CreditLimit: 25_000m);

    [Theory]
    [InlineData("PM")]
    [InlineData("Engineer")]
    public async Task Roles_without_commercial_access_cannot_change_credit_or_tax_terms(string role)
    {
        var result = await ControllerFor(role).UpdateCustomer(1, CreditChange());

        result.Should().BeOfType<ForbidResult>();
        _mediator.Verify(m => m.Send(It.IsAny<UpdateCustomerCommand>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Theory]
    [InlineData("Admin")]
    [InlineData("Manager")]
    [InlineData("OfficeManager")]
    public async Task Commercial_roles_change_credit_and_tax_terms(string role)
    {
        var result = await ControllerFor(role).UpdateCustomer(1,
            new UpdateCustomerRequestModel(null, null, null, null, null,
                IsTaxExempt: true, TaxExemptionId: "UT-1", CreditLimit: 25_000m));

        result.Should().BeOfType<NoContentResult>();
        _mediator.Verify(m => m.Send(
            It.Is<UpdateCustomerCommand>(c => c.CreditLimit == 25_000m && c.IsTaxExempt == true && c.TaxExemptionId == "UT-1"),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Pm_can_still_edit_identity_fields()
    {
        var result = await ControllerFor("PM").UpdateCustomer(1,
            new UpdateCustomerRequestModel("Acme", null, null, null, null));

        result.Should().BeOfType<NoContentResult>();
    }
}
