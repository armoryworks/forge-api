using System.Security.Claims;

using FluentAssertions;
using MediatR;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Moq;

using Forge.Api.Controllers;
using Forge.Api.Features.Customers;
using Forge.Api.Features.Leads;
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

    [Fact]
    public async Task Pm_cannot_create_a_customer_with_credit_or_tax_terms()
    {
        var result = await ControllerFor("PM").CreateCustomer(
            new CreateCustomerRequestModel("Acme", null, null, null, CreditLimit: 25_000m));

        result.Result.Should().BeOfType<ForbidResult>();
        _mediator.Verify(m => m.Send(It.IsAny<CreateCustomerCommand>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Pm_can_create_a_customer_with_default_terms()
    {
        _mediator.Setup(m => m.Send(It.IsAny<CreateCustomerCommand>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new CustomerListItemModel(7, "Acme", null, null, null, null, true, 0, 0, DateTimeOffset.UtcNow));

        var result = await ControllerFor("PM").CreateCustomer(new CreateCustomerRequestModel("Acme", null, null, null));

        result.Result.Should().BeOfType<CreatedAtActionResult>();
    }

    private static CustomerDetailResponseModel DetailWithCreditLimit() =>
        new(1, "Acme", null, null, null, true, false, null, null, null, null,
            DateTimeOffset.UtcNow, DateTimeOffset.UtcNow, [], [], CreditLimit: 25_000m);

    [Theory]
    [InlineData("PM", null)]
    [InlineData("Engineer", null)]
    [InlineData("OfficeManager", 25_000)]
    public async Task Customer_detail_shows_the_credit_limit_only_to_commercial_roles(string role, int? expected)
    {
        _mediator.Setup(m => m.Send(It.IsAny<GetCustomerByIdQuery>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(DetailWithCreditLimit());

        var result = await ControllerFor(role).GetCustomer(1);

        var detail = result.Result.Should().BeOfType<OkObjectResult>().Which.Value.Should().BeOfType<CustomerDetailResponseModel>().Which;
        detail.CreditLimit.Should().Be(expected);
    }

    [Theory]
    [InlineData("PM", null)]
    [InlineData("Manager", 25_000)]
    public async Task Customer_summary_shows_the_credit_limit_only_to_commercial_roles(string role, int? expected)
    {
        _mediator.Setup(m => m.Send(It.IsAny<GetCustomerSummaryQuery>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new CustomerSummaryResponseModel(1, "Acme", null, null, null, null, true, null, null, null,
                DateTimeOffset.UtcNow, DateTimeOffset.UtcNow, 0, 0, 0, 0, 0, 0m, 0m, CreditLimit: 25_000m));

        var result = await ControllerFor(role).GetSummary(1);

        var summary = result.Result.Should().BeOfType<OkObjectResult>().Which.Value.Should().BeOfType<CustomerSummaryResponseModel>().Which;
        summary.CreditLimit.Should().Be(expected);
    }

    [Fact]
    public async Task Pm_cannot_set_credit_or_tax_terms_while_converting_a_lead()
    {
        var leads = new LeadsController(_mediator.Object) { ControllerContext = ControllerFor("PM").ControllerContext };

        var result = await leads.ConvertLead(1, new ConvertLeadRequestModel(false, CreditLimit: 10_000m));

        result.Result.Should().BeOfType<ForbidResult>();
        _mediator.Verify(m => m.Send(It.IsAny<ConvertLeadCommand>(), It.IsAny<CancellationToken>()), Times.Never);
    }
}
