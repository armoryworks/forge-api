using System.Reflection;

using FluentAssertions;
using Microsoft.AspNetCore.Authorization;

using Forge.Api.Controllers;

namespace Forge.Tests.Authorization;

/// <summary>
/// The payment terms list is read by the finance, purchasing and accounting roles only: a PM never sees payment
/// terms (2026-10-16 rule), and the server applies the customer's terms to a PM's quotes and orders.
/// </summary>
public sealed class PaymentTermsReadRoleTests
{
    private static readonly string[] AllRoles =
    [
        "Admin", "Manager", "Engineer", "PM", "ProductionWorker", "OfficeManager",
        "Controller", "IT Admin", "Procurement", "Production Manager", "Production Planner",
        "ComplianceOfficer",
    ];

    private static HashSet<string> EffectiveRoles(string actionName)
    {
        var action = typeof(PaymentTermsController).GetMethod(actionName)!;
        var allowed = new HashSet<string>(AllRoles, StringComparer.Ordinal);
        var attributes = typeof(PaymentTermsController).GetCustomAttributes<AuthorizeAttribute>()
            .Concat(action.GetCustomAttributes<AuthorizeAttribute>());
        foreach (var attribute in attributes.Where(a => !string.IsNullOrEmpty(a.Roles)))
            allowed.IntersectWith(attribute.Roles!.Split(',').Select(r => r.Trim()));
        return allowed;
    }

    [Theory]
    [InlineData("Admin")]
    [InlineData("Manager")]
    [InlineData("OfficeManager")]
    [InlineData("Procurement")]
    [InlineData("Controller")]
    public void Finance_purchasing_and_accounting_roles_read_the_terms_list(string role)
        => EffectiveRoles(nameof(PaymentTermsController.GetPaymentTerms)).Should().Contain(role);

    [Theory]
    [InlineData("PM")]
    [InlineData("Engineer")]
    [InlineData("ProductionWorker")]
    public void Other_roles_are_refused_the_terms_list(string role)
        => EffectiveRoles(nameof(PaymentTermsController.GetPaymentTerms)).Should().NotContain(role);

    [Fact]
    public void Maintaining_terms_stays_with_the_finance_roles()
        => EffectiveRoles(nameof(PaymentTermsController.CreatePaymentTerm))
            .Should().BeEquivalentTo(["Admin", "Manager", "OfficeManager"]);
}
