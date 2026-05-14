
namespace Forge.Core.Models;

public record QueueLeadResponseModel(
    int Id,
    string CompanyName,
    string? ContactName,
    string? Email,
    string? Phone,
    string? Source,
    string? Notes,
    LeadStatus Status,
    OutreachState OutreachState,
    int? CampaignId,
    string? CampaignName,
    DateTimeOffset? LastActivityAt,
    /// <summary>Set when an active cooldown blocks the lead — UI surfaces this so reps know not to push.</summary>
    DateTimeOffset? CooldownUntil,
    bool EmailOptOut,
    bool CallOptOut);

public record DispositionLeadRequest(
    OutreachState NextState,
    /// <summary>Free-text reason / notes captured per disposition. Falls into the resulting ContactInteraction body.</summary>
    string? Notes,
    /// <summary>For CallbackScheduled — when the prospect wants to be re-contacted.</summary>
    DateTimeOffset? CallbackAt);

/// <summary>
/// Phase 1r / Batch 6 — pull request from the worker. The seller asks
/// for the next N leads; the queue serves disjoint slices using
/// FOR UPDATE SKIP LOCKED semantics (Postgres-native) so two reps
/// can pull simultaneously without dialing the same lead.
/// </summary>
public record PullQueueRequest(
    int? CampaignId,
    int Count = 1);
