using System.Security.Claims;

using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

using Forge.Api.Features.Accounting.Sod;
using Forge.Core.Enums.Accounting;
using Forge.Core.Interfaces;
using Forge.Core.Models.Accounting;

namespace Forge.Tests.Accounting;

/// <summary>
/// §5.7 — segregation of duties enforced at the engine boundary against the
/// caller's <b>effective</b> capability set. GL capabilities attach to
/// <c>Controller</c> (or any rollup that composes it, which presents the
/// Controller role claim via RoleClaimsExpander); bare Admin/Manager are off the
/// books. The boundary authorizer is fail-safe default-deny.
/// </summary>
public class GlSegregationOfDutiesTests
{
    private const int UserId = 7;

    private static IHttpContextAccessor AccessorWith(int? userId, params string[] roles)
    {
        var claims = new List<Claim>();
        if (userId is int id) claims.Add(new Claim(ClaimTypes.NameIdentifier, id.ToString()));
        claims.AddRange(roles.Select(r => new Claim(ClaimTypes.Role, r)));

        // "Test" authenticationType makes Identity.IsAuthenticated == true.
        var principal = new ClaimsPrincipal(new ClaimsIdentity(claims, authenticationType: "Test"));
        var accessor = new Mock<IHttpContextAccessor>();
        accessor.Setup(a => a.HttpContext).Returns(new DefaultHttpContext { User = principal });
        return accessor.Object;
    }

    private static IHttpContextAccessor NoContext()
    {
        var accessor = new Mock<IHttpContextAccessor>();
        accessor.Setup(a => a.HttpContext).Returns((HttpContext?)null);
        return accessor.Object;
    }

    private static GlBoundaryAuthorizer AuthorizerFor(IHttpContextAccessor accessor) =>
        new(new CurrentUserCapabilities(accessor), NullLogger<GlBoundaryAuthorizer>.Instance);

    [Fact]
    public void Controller_HoldsEveryGlCapability()
    {
        var caps = new CurrentUserCapabilities(AccessorWith(UserId, "Controller"));

        caps.CurrentUserId.Should().Be(UserId);
        foreach (var cap in Enum.GetValues<GlCapability>())
            caps.Has(cap).Should().BeTrue($"Controller should hold {cap} (§5.7)");
    }

    [Theory]
    [InlineData("Admin")]
    [InlineData("Manager")]
    [InlineData("OfficeManager")]
    public void BareNonControllerRoles_HoldNoGlCapability(string role)
    {
        var caps = new CurrentUserCapabilities(AccessorWith(UserId, role));

        foreach (var cap in Enum.GetValues<GlCapability>())
            caps.Has(cap).Should().BeFalse($"bare {role} must be off the books (§5.7)");
    }

    [Fact]
    public void OwnerOperatorRollup_ReachesBooksViaComposedControllerClaim()
    {
        // RoleClaimsExpander expands OwnerOperator ["Admin","Manager","Controller"]
        // into individual role claims; the authorizer sees Controller.
        var caps = new CurrentUserCapabilities(AccessorWith(UserId, "Admin", "Manager", "Controller"));

        caps.Has(GlCapability.PostJournalEntry).Should().BeTrue();
        // …and it trips the toxic-combination probe intentionally + visibly.
        caps.HasToxicPostingCombination().Should().BeTrue();
    }

    [Fact]
    public void BareController_DoesNotTripToxicCombination()
    {
        var caps = new CurrentUserCapabilities(AccessorWith(UserId, "Controller"));
        caps.HasToxicPostingCombination().Should().BeFalse();
    }

    [Fact]
    public void Authorizer_AllowsController()
    {
        var authorizer = AuthorizerFor(AccessorWith(UserId, "Controller"));
        authorizer.Invoking(a => a.EnsureAuthorized(GlCapability.PostJournalEntry))
            .Should().NotThrow();
    }

    [Fact]
    public void Authorizer_DeniesNonController()
    {
        var authorizer = AuthorizerFor(AccessorWith(UserId, "Admin"));
        authorizer.Invoking(a => a.EnsureAuthorized(GlCapability.PostJournalEntry))
            .Should().Throw<GlAuthorizationException>()
            .Which.RequiredCapability.Should().Be(GlCapability.PostJournalEntry);
    }

    [Fact]
    public void Authorizer_FailSafeDeniesWhenNoPrincipal()
    {
        // No NameIdentifier → CurrentUserId null → fail-safe deny.
        var authorizer = AuthorizerFor(NoContext());
        authorizer.Invoking(a => a.EnsureAuthorized(GlCapability.ReverseJournalEntry))
            .Should().Throw<GlAuthorizationException>();
    }
}
