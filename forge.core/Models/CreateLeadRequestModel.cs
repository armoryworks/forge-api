using Forge.Core.Enums;

namespace Forge.Core.Models;

public record CreateLeadRequestModel(
    string CompanyName,
    string? ContactName,
    string? Email,
    string? Phone,
    string? Source,
    string? Notes,
    DateTimeOffset? FollowUpDate,
    // Wave 7 — engagement-shape axis from the New Lead fork dialog. Defaults
    // to Unknown so the "Quick add" path (skip the axis) lands a valid
    // payload without forcing a value at creation. CustomFieldValues stays
    // free-form JSONB; the fork's per-shape specialised fields land there.
    LeadEngagementShape EngagementShape = LeadEngagementShape.Unknown,
    string? CustomFieldValues = null,
    // Phase 1r / Batch 12 — optional B2B parent account at intake. Lets
    // the fork dialog group new leads under an existing Account without a
    // follow-up edit. Null = unaffiliated (legacy flat-lead shape).
    int? AccountId = null,
    // Intake idempotency key: the relaying system's stable id for this
    // submission (e.g. Tuyere's submission id). When a live lead already
    // carries the same value, CreateLead returns that lead (200) instead of
    // creating a duplicate — POST retries after a timeout/5xx are safe.
    // Null for interactive creates.
    string? ExternalId = null,
    // Formal source attribution: a lead_sources.code (e.g. "armoryworks.com").
    // Resolved server-side to LeadSourceId; an unknown or missing code leaves
    // LeadSourceId null rather than failing the create (the free-text Source
    // field still lands either way).
    string? LeadSourceCode = null,
    // Optional caller-supplied lead number. Honoured only when
    // leads.allow_manual_numbers is on; otherwise auto-generated (LEAD-#####).
    string? LeadNumber = null);
