namespace Forge.Core.Entities;

// Many-to-many: which workers are qualified to operate which work centers.
// Drives kiosk job-list filtering (Bob only sees jobs whose current op
// runs on a center he's signed off on) and assignment validation (Mike
// can't assign a job to Bob if Bob isn't qualified on the bottleneck
// operation's center).
//
// Composite PK on (UserId, WorkCenterId) — a user is either qualified on
// a center or isn't; no duplicates. Use Notes for sign-off context
// ("trained 2026-03-12 by Mike, observed 8 hrs"). Future: certification
// expiry and re-qualification workflow.
public class WorkCenterQualification
{
    public int UserId { get; set; }
    public int WorkCenterId { get; set; }
    public DateTimeOffset QualifiedAt { get; set; } = DateTimeOffset.UtcNow;
    public int? QualifiedById { get; set; }
    public string? Notes { get; set; }

    public WorkCenter WorkCenter { get; set; } = null!;
}
