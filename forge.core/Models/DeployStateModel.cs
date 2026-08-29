namespace Forge.Core.Models;

/// <summary>One versioned tier of the install, as the deploy agent reports it.</summary>
/// <param name="Service">Deploy service id — api, ui, test, demo.</param>
/// <param name="Running">Image tag actually running, from the container's image.</param>
/// <param name="Configured">Tag recorded by the last successful deploy. Divergence from
/// <paramref name="Running"/> means the box was changed outside the gated deploy path.</param>
/// <param name="DeployedAt">When the recorded tag was deployed.</param>
public record DeployTierModel(
    string Service,
    string? Running,
    string? Configured,
    DateTimeOffset? DeployedAt);

/// <summary>
/// What this box is running and whether an upgrade is in flight. <c>AgentAvailable</c> false is
/// the normal state for a cohosted or centrally managed instance, not an error — upgrades are
/// simply not this tenant's to run, and the caller should say so rather than offer a broken button.
/// </summary>
public record DeployStateModel(
    bool AgentAvailable,
    string? AgentVersion,
    IReadOnlyList<DeployTierModel> Tiers,
    DeployJobModel? RunningJob);
