using System.Security.Claims;

using FluentAssertions;

using Forge.Api.Workflows;

namespace Forge.Tests.Workflows;

public class WorkflowEntityEditAccessTests
{
    private static ClaimsPrincipal User(params string[] roles) =>
        new(new ClaimsIdentity(
            roles.Select(r => new Claim(ClaimTypes.Role, r)).Append(new Claim(ClaimTypes.NameIdentifier, "7")),
            "test"));

    [Theory]
    [InlineData("Vendor", "OfficeManager", true)]
    [InlineData("Vendor", "Admin", true)]
    [InlineData("Vendor", "ProductionWorker", false)]
    [InlineData("Vendor", "Engineer", false)]
    [InlineData("Customer", "PM", true)]
    [InlineData("Customer", "ProductionWorker", false)]
    [InlineData("Part", "Engineer", true)]
    public void CanEdit_FollowsTheEntityUpdateEndpointRoles(string entityType, string role, bool expected)
    {
        WorkflowEntityEditAccess.CanEdit(User(role), entityType).Should().Be(expected);
    }

    [Fact]
    public void CanEdit_IsFalse_ForAnUnmappedEntityType_OrAnAnonymousCaller()
    {
        WorkflowEntityEditAccess.CanEdit(User("Admin"), "Employee").Should().BeFalse();
        WorkflowEntityEditAccess.CanEdit(new ClaimsPrincipal(new ClaimsIdentity()), "Vendor").Should().BeFalse();
        WorkflowEntityEditAccess.CanEdit(null, "Vendor").Should().BeFalse();
    }
}
