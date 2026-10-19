using System.Diagnostics;
using System.Net;
using System.Text;

using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;

using Forge.Api.Services;

namespace Forge.Tests.Services;

/// <summary>
/// A hung forge-agent must not hold the Updates screen: the state read (state plus current job) shares one
/// short budget and falls back to the unavailable shape when it runs out.
/// </summary>
public class DeployAgentClientStateBudgetTests
{
    private sealed class ScriptedHandler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> respond) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            => respond(request, cancellationToken);
    }

    private static DeployAgentClient Client(HttpMessageHandler handler, string budgetSeconds = "1")
    {
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Deploy:AgentUrl"] = "http://agent.test/",
            ["Deploy:StateTimeoutSeconds"] = budgetSeconds,
        }).Build();
        var http = new HttpClient(handler) { BaseAddress = new Uri("http://agent.test/"), Timeout = TimeSpan.FromSeconds(90) };
        return new DeployAgentClient(http, config, NullLogger<DeployAgentClient>.Instance);
    }

    private static HttpResponseMessage Json(string body) =>
        new(HttpStatusCode.OK) { Content = new StringContent(body, Encoding.UTF8, "application/json") };

    [Fact]
    public async Task Hung_agent_reports_unavailable_within_the_state_budget()
    {
        var client = Client(new ScriptedHandler(async (_, ct) =>
        {
            await Task.Delay(TimeSpan.FromSeconds(30), ct);
            return Json("{}");
        }));

        var sw = Stopwatch.StartNew();
        var state = await client.GetStateAsync(CancellationToken.None);
        sw.Stop();

        state.AgentAvailable.Should().BeFalse();
        sw.Elapsed.Should().BeLessThan(TimeSpan.FromSeconds(10));
    }

    [Fact]
    public async Task The_budget_covers_the_state_and_current_job_calls_together()
    {
        var client = Client(new ScriptedHandler(async (request, ct) =>
        {
            if (request.RequestUri!.AbsolutePath.EndsWith("/state"))
            {
                await Task.Delay(TimeSpan.FromMilliseconds(600), ct);
                return Json("""{"agentVersion":"1.0","state":{},"running":{"id":"j1"}}""");
            }
            await Task.Delay(TimeSpan.FromSeconds(30), ct);
            return Json("{}");
        }));

        var sw = Stopwatch.StartNew();
        var state = await client.GetStateAsync(CancellationToken.None);
        sw.Stop();

        state.AgentAvailable.Should().BeTrue();
        state.RunningJob.Should().BeNull();
        sw.Elapsed.Should().BeLessThan(TimeSpan.FromSeconds(5));
    }

    [Fact]
    public async Task Responsive_agent_still_reports_its_state()
    {
        var client = Client(new ScriptedHandler((_, _) =>
            Task.FromResult(Json("""{"agentVersion":"2.1","state":{}}"""))), budgetSeconds: "");

        var state = await client.GetStateAsync(CancellationToken.None);

        state.AgentAvailable.Should().BeTrue();
        state.AgentVersion.Should().Be("2.1");
    }
}
