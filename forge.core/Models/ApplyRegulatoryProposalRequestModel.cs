namespace Forge.Core.Models;

/// <summary>
/// regulatory-watchtower A-8: optional apply payload — a due date + target Event-Type turn the
/// proposal into a system-generated compliance-calendar deadline on confirm.
/// </summary>
public record ApplyRegulatoryProposalRequestModel(DateTimeOffset? DueDate, int? TargetEventTypeId);
