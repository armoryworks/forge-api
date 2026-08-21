using System.Linq;

namespace Forge.Core.Models;

/// <summary>Fleet-wide integration readiness for the current install: the posture,
/// whether production is (mis)running on mocks, and the per-integration verdicts.</summary>
public sealed record IntegrationReadinessReport(
    bool ProductionPosture,
    bool MockIntegrationsInProduction,
    IReadOnlyList<IntegrationReadiness> Integrations)
{
    /// <summary>Integrations whose capability is ON but which are unconfigured in a
    /// production posture — the actionable "configure or disable the capability" set.</summary>
    public IReadOnlyList<IntegrationReadiness> Gaps
        => Integrations.Where(i => i.Status == IntegrationReadinessStatus.Gap).ToList();
}
