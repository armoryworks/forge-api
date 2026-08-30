using Forge.Core.Models;

namespace Forge.Core.Interfaces;

/// <summary>
/// Talks to forge-agent, the privileged host process that runs the gated deploy CLI.
/// <para>
/// The API never touches docker itself: forge-api and forge-ui are precisely the containers an
/// upgrade destroys, so neither can supervise its own replacement, and handing a public-facing
/// web app the docker socket would trade a root-equivalent boundary for a convenience button.
/// This client decides <i>who may upgrade</i> and audits it; the agent decides nothing and
/// executes fixed argv.
/// </para>
/// <para>
/// Every method degrades to a value rather than an exception when the agent is absent or
/// unreachable — "no agent on this box" is a supported deployment, not a fault.
/// </para>
/// </summary>
public interface IDeployAgentClient
{
    /// <summary>False when Deploy:AgentUrl is unset — upgrades are not available from the app.</summary>
    bool IsConfigured { get; }

    /// <summary>Running versus recorded tags per tier, plus any job already in flight.</summary>
    Task<DeployStateModel> GetStateAsync(CancellationToken ct);

    /// <summary>Whether a newer release exists for every tier. Never reports unreachable as current.</summary>
    Task<DeployAvailabilityModel> CheckAvailableAsync(CancellationToken ct);

    /// <summary>Starts a job. Returns status busy when one is already running — the agent is single-flight.</summary>
    Task<DeployJobStartResultModel> StartJobAsync(
        string action, string? service, string? tag, string? confirm, CancellationToken ct);

    /// <summary>One job by id, or null if the agent has pruned or never had it.</summary>
    Task<DeployJobModel?> GetJobAsync(string jobId, CancellationToken ct);

    /// <summary>The job currently running on this box, or null.</summary>
    Task<DeployJobModel?> GetCurrentJobAsync(CancellationToken ct);

    /// <summary>The most recently started job, running or finished, or null if the agent has none.</summary>
    Task<DeployJobModel?> GetLastJobAsync(CancellationToken ct);

    /// <summary>Deploy log from a byte offset, for incremental append in the admin console.</summary>
    Task<string> GetJobLogAsync(string jobId, long offset, CancellationToken ct);
}
