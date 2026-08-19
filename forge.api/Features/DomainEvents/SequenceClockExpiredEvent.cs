using MediatR;

using Forge.Core.Enums;

namespace Forge.Api.Features.DomainEvents;

/// <summary>
/// Gated Sequence Engine — a resource clock or a step dwell clock expired. Fired exactly once per clock (the job
/// stamps FiredAt). <paramref name="Action"/> tells consumers whether this is informational (Flag), blocking (Block),
/// or needs routing to <paramref name="EscalateRole"/> (Escalate).
/// </summary>
public record SequenceClockExpiredEvent(
    string ClockKind,            // "resource" | "dwell"
    int? InstanceId,
    string? StepKey,
    string? ResourceType,
    int? ResourceId,
    SequenceExpiryAction Action,
    string? EscalateRole,
    DateTimeOffset ExpiredAt) : INotification;
