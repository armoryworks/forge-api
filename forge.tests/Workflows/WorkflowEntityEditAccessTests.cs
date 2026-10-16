using System.Security.Claims;

using FluentAssertions;

using Forge.Api.Middleware;
using Forge.Api.Workflows;
using Forge.Core.Entities;

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

    [Theory]
    [InlineData("Vendor", "OfficeManager", true)]
    [InlineData("Vendor", "Procurement", true)]
    [InlineData("Vendor", "ProductionWorker", false)]
    [InlineData("Vendor", "Engineer", false)]
    [InlineData("Customer", "PM", true)]
    [InlineData("Customer", "OfficeManager", true)]
    [InlineData("Customer", "ProductionWorker", false)]
    [InlineData("Part", "OfficeManager", true)]
    [InlineData("Part", "Engineer", true)]
    [InlineData("Part", "Procurement", false)]
    public void CanCreate_FollowsTheEntityCreateEndpointRoles(string entityType, string role, bool expected)
    {
        WorkflowEntityEditAccess.CanCreate(User(role), entityType).Should().Be(expected);
    }

    [Fact]
    public void CanCreate_IsFalse_ForAnUnmappedEntityType_OrAnAnonymousCaller()
    {
        WorkflowEntityEditAccess.CanCreate(User("Admin"), "Employee").Should().BeFalse();
        WorkflowEntityEditAccess.CanCreate(new ClaimsPrincipal(new ClaimsIdentity()), "Vendor").Should().BeFalse();
        WorkflowEntityEditAccess.CanCreate(null, "Vendor").Should().BeFalse();
    }

    [Theory]
    [InlineData(7, "OfficeManager", null, true)]
    [InlineData(7, "OfficeManager", 12, true)]
    [InlineData(7, "Engineer", null, false)]
    [InlineData(7, "Engineer", 12, false)]
    [InlineData(99, "Manager", null, true)]
    [InlineData(99, "Manager", 12, true)]
    [InlineData(99, "ProductionWorker", null, false)]
    [InlineData(99, "Engineer", 12, false)]
    public void CanAccessRun_AllowsTheStarterWithAccess_OrAnEditorOfTheEntityType(
        int startedByUserId, string role, int? entityId, bool expected)
    {
        var run = new WorkflowRun { EntityType = "Vendor", EntityId = entityId, StartedByUserId = startedByUserId };

        WorkflowEntityEditAccess.CanAccessRun(User(role), 7, run).Should().Be(expected);
    }

    [Fact]
    public void EnsureCanAccessRun_ThrowsForbidden_ForAnotherUsersRunWithoutEditRoles()
    {
        var run = new WorkflowRun { EntityType = "Vendor", StartedByUserId = 99 };

        var act = () => WorkflowEntityEditAccess.EnsureCanAccessRun(User("Engineer"), 7, run);

        act.Should().Throw<ForbiddenException>();
    }
}
