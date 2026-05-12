namespace Forge.Core.Models;

/// <summary>
/// Workflow Pattern Phase 3 — Promote an entity from one status to another
/// (v1 wires Draft → Active for Part). The authoritative readiness gate
/// runs server-side; failures return 409 with a missing-validators list.
/// </summary>
public record PromoteEntityStatusRequestModel(string TargetStatus);
