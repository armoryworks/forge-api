using System.Reflection;
using System.Security.Claims;

using FluentAssertions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.DependencyInjection;

using Forge.Api.Authorization;
using Forge.Api.Controllers;
using Forge.Api.Hubs;

namespace Forge.Tests.Hubs;

public class BoardHubAuthorizationTests
{
    private static string? RolesOn(MemberInfo member)
        => member.GetCustomAttributes<AuthorizeAttribute>().Should().ContainSingle().Which.Roles;

    [Fact]
    public void Joining_a_board_needs_the_same_roles_as_loading_the_board()
    {
        RolesOn(typeof(BoardHub).GetMethod(nameof(BoardHub.JoinBoard))!).Should().Be(JobRoles.BoardRead);
        RolesOn(typeof(JobListController)).Should().Be(JobRoles.BoardRead);
    }

    [Fact]
    public void Joining_a_job_needs_the_same_roles_as_reading_a_job()
    {
        RolesOn(typeof(BoardHub).GetMethod(nameof(BoardHub.JoinJob))!).Should().Be(JobRoles.Read);
        RolesOn(typeof(JobsController)).Should().Be(JobRoles.Read);
    }

    [Fact]
    public void The_hub_itself_admits_every_signed_in_user_so_negotiate_never_403s()
    {
        RolesOn(typeof(BoardHub)).Should().BeNull();
    }

    [Theory]
    [InlineData(nameof(BoardHub.LeaveBoard))]
    [InlineData(nameof(BoardHub.LeaveJob))]
    public void Leaving_a_group_carries_no_role_gate(string method)
    {
        typeof(BoardHub).GetMethod(method)!.GetCustomAttributes<AuthorizeAttribute>().Should().BeEmpty();
    }

    [Theory]
    [InlineData("Procurement", nameof(BoardHub.JoinBoard), true)]
    [InlineData("Procurement", nameof(BoardHub.JoinJob), false)]
    [InlineData("ProductionWorker", nameof(BoardHub.JoinJob), true)]
    [InlineData("Engineer", nameof(BoardHub.JoinBoard), true)]
    [InlineData("Controller", nameof(BoardHub.JoinBoard), false)]
    [InlineData("Controller", nameof(BoardHub.JoinJob), false)]
    public async Task A_join_is_allowed_only_for_roles_that_can_read_the_same_data(string role, string method, bool allowed)
    {
        var services = new ServiceCollection().AddLogging().AddAuthorization().BuildServiceProvider();
        var policyProvider = services.GetRequiredService<IAuthorizationPolicyProvider>();
        var authorization = services.GetRequiredService<IAuthorizationService>();
        var attributes = typeof(BoardHub).GetCustomAttributes<AuthorizeAttribute>()
            .Concat(typeof(BoardHub).GetMethod(method)!.GetCustomAttributes<AuthorizeAttribute>());
        var policy = await AuthorizationPolicy.CombineAsync(policyProvider, attributes);
        var user = new ClaimsPrincipal(new ClaimsIdentity([new Claim(ClaimTypes.Role, role)], "test"));

        var result = await authorization.AuthorizeAsync(user, policy!);

        result.Succeeded.Should().Be(allowed);
    }
}
