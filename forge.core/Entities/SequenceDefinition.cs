using Forge.Core.Enums;

namespace Forge.Core.Entities;

/// <summary>
/// Gated Sequence Engine — a versioned, immutable-once-published process template: steps, the edges between
/// them, and the gates each step must pass. Natural key is (<see cref="Code"/>, <see cref="Version"/>); a new
/// version is a new row. Instances pin the version they started on (publishing v2 never touches v1 runs).
/// </summary>
public class SequenceDefinition : BaseAuditableEntity
{
    /// <summary>Stable code shared by all versions, e.g. "job-routing-standard".</summary>
    public string Code { get; set; } = string.Empty;

    public int Version { get; set; } = 1;

    public string Name { get; set; } = string.Empty;

    public string? Description { get; set; }

    /// <summary>Optional: the entity type instances of this definition run against (e.g. "Job"). Null = any/none.</summary>
    public string? SubjectEntityType { get; set; }

    public SequenceDefinitionStatus Status { get; set; } = SequenceDefinitionStatus.Draft;

    /// <summary>
    /// When true and Published, a run starts automatically for every newly created subject of
    /// <see cref="SubjectEntityType"/> (today: "Job" via <c>JobCreatedEvent</c>). One definition per code — the
    /// latest published version wins.
    /// </summary>
    public bool AutoStartOnSubjectCreate { get; set; }

    public DateTimeOffset? PublishedAt { get; set; }

    public int? PublishedByUserId { get; set; }

    public ICollection<SequenceStepDefinition> Steps { get; set; } = [];

    public ICollection<SequenceEdgeDefinition> Edges { get; set; } = [];

    public ICollection<SequenceGateDefinition> Gates { get; set; } = [];
}
