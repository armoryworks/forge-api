using Forge.Core.Enums;
using Forge.Core.Entities.Calendar;

namespace Forge.Core.Entities.Regulatory;

/// <summary>
/// regulatory-watchtower + compliance-calendar A-5/A-8: a proposed regulatory change surfaced
/// by the poller. **Propose-and-confirm** — an admin reviews and applies (e.g. creates/updates a
/// compliance calendar deadline) or dismisses. Never auto-applied (guards the I-9 "36 hours" trap).
/// </summary>
public class RegulatoryChangeProposal : BaseAuditableEntity
{
    public int RegulatorySourceId { get; set; }
    public RegulatorySource RegulatorySource { get; set; } = null!;

    public string Title { get; set; } = string.Empty;
    public string? SummaryUrl { get; set; }

    /// <summary>Free-form details / JSON payload of the proposed change.</summary>
    public string? Details { get; set; }

    public RegulatoryProposalStatus Status { get; set; } = RegulatoryProposalStatus.Pending;

    /// <summary>Optional calendar Event-Type the proposal relates to (for apply).</summary>
    public int? TargetEventTypeId { get; set; }
    public CalendarEventType? TargetEventType { get; set; }

    public int? ReviewedByUserId { get; set; }
    public DateTimeOffset? ReviewedAt { get; set; }
}
