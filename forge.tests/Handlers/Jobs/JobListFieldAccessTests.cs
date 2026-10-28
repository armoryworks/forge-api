using System.Security.Claims;

using FluentAssertions;
using FluentValidation;
using Microsoft.AspNetCore.Http;
using Moq;

using Forge.Api.Features.Jobs;
using Forge.Core.Interfaces;
using Forge.Core.Models;
using Forge.Tests.Helpers;

namespace Forge.Tests.Handlers.Jobs;

public class JobListFieldAccessTests
{
    private readonly JobOperationTestHarness _h = new();
    private readonly Mock<IJobRepository> _repo = new();

    private static ClaimsPrincipal User(params string[] roles) =>
        new(new ClaimsIdentity(
            roles.Select(r => new Claim(ClaimTypes.Role, r)).Append(new Claim(ClaimTypes.NameIdentifier, "7")),
            "test"));

    private GetJobsHandler Handler(ClaimsPrincipal user) =>
        new(_repo.Object, _h.Operations, new HttpContextAccessor { HttpContext = new DefaultHttpContext { User = user } });

    [Theory]
    [InlineData("Admin", true, true, true, true)]
    [InlineData("OfficeManager", true, true, true, true)]
    [InlineData("PM", true, true, false, true)]
    [InlineData("Engineer", true, false, false, true)]
    [InlineData("ProductionWorker", true, false, false, false)]
    [InlineData("Production Planner", true, true, false, false)]
    [InlineData("Procurement", false, false, false, false)]
    public void Each_role_reads_the_field_groups_its_owning_endpoints_admit(
        string role, bool customer, bool salesOrder, bool billing, bool cost)
    {
        JobListFieldAccessResolver.For(User(role))
            .Should().Be(new JobListFieldAccess(customer, salesOrder, billing, cost));
    }

    [Fact]
    public void A_request_without_a_signed_in_user_is_a_system_call()
    {
        JobListFieldAccessResolver.For(null).Should().Be(JobListFieldAccess.Full);
    }

    [Fact]
    public async Task The_handler_hands_the_callers_access_to_the_repository()
    {
        var query = new JobListQuery { TrackTypeId = 1 };
        _repo.Setup(r => r.GetPagedJobsAsync(query, It.IsAny<bool>(), It.IsAny<JobListFieldAccess>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PagedResponse<JobListResponseModel>([], 0, 1, 25));

        await Handler(User("Procurement")).Handle(new GetJobsQuery(query), CancellationToken.None);

        _repo.Verify(r => r.GetPagedJobsAsync(query, It.IsAny<bool>(),
            new JobListFieldAccess(false, false, false, false), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Theory]
    [InlineData("Procurement", "customerName:contains:acme")]
    [InlineData("ProductionWorker", "salesOrderNumber:contains:SO")]
    [InlineData("Engineer", "billingStatus:in:Invoiced")]
    [InlineData("ProductionWorker", "quotedPrice:range:1..")]
    [InlineData("Admin", "description:contains:x")]
    [InlineData("Admin", "jobNumber:between:1..2")]
    public async Task A_filter_the_caller_may_not_use_is_a_bad_request(string role, string filter)
    {
        var act = () => Handler(User(role)).Handle(
            new GetJobsQuery(new JobListQuery { Filter = [filter] }), CancellationToken.None);

        await act.Should().ThrowAsync<ValidationException>();
        _repo.Verify(r => r.GetPagedJobsAsync(It.IsAny<JobListQuery>(), It.IsAny<bool>(),
            It.IsAny<JobListFieldAccess>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task A_refused_gated_field_reads_the_same_as_an_unknown_one()
    {
        var gated = await Message(User("Procurement"), "customerName:contains:acme");
        var unknown = await Message(User("Procurement"), "customerNameX:contains:acme");

        gated.Should().Be(unknown.Replace("customerNameX", "customerName"));
    }

    private async Task<string> Message(ClaimsPrincipal user, string filter)
    {
        try
        {
            await Handler(user).Handle(new GetJobsQuery(new JobListQuery { Filter = [filter] }), CancellationToken.None);
        }
        catch (ValidationException ex)
        {
            return ex.Errors.Single().ErrorMessage;
        }
        return string.Empty;
    }
}
