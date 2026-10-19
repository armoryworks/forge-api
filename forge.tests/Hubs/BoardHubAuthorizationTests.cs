using System.Reflection;

using FluentAssertions;
using Microsoft.AspNetCore.Authorization;

using Forge.Api.Authorization;
using Forge.Api.Controllers;
using Forge.Api.Hubs;

namespace Forge.Tests.Hubs;

public class BoardHubAuthorizationTests
{
    [Theory]
    [InlineData(nameof(BoardHub.JoinBoard))]
    [InlineData(nameof(BoardHub.JoinJob))]
    public void Joining_a_job_group_needs_the_same_roles_as_reading_a_job(string method)
    {
        var roles = typeof(BoardHub).GetMethod(method)!.GetCustomAttributes<AuthorizeAttribute>()
            .Select(a => a.Roles).ToList();

        roles.Should().Equal(JobRoles.Read);
        typeof(JobsController).GetCustomAttributes<AuthorizeAttribute>()
            .Select(a => a.Roles).Should().Contain(JobRoles.Read);
    }

    [Fact]
    public void The_hub_itself_admits_every_signed_in_user_so_negotiate_never_403s()
    {
        typeof(BoardHub).GetCustomAttributes<AuthorizeAttribute>()
            .Should().ContainSingle().Which.Roles.Should().BeNull();
    }

    [Theory]
    [InlineData(nameof(BoardHub.LeaveBoard))]
    [InlineData(nameof(BoardHub.LeaveJob))]
    public void Leaving_a_group_carries_no_role_gate(string method)
    {
        typeof(BoardHub).GetMethod(method)!.GetCustomAttributes<AuthorizeAttribute>().Should().BeEmpty();
    }
}
