namespace Forge.Core.Entities;

/// <summary>
/// Pro Services rollout (Artifact 4 §4.6) — lightweight artifact tracking
/// for engagement work. A deliverable is a "thing produced and given to a
/// client" (report, code, design, documentation, training, other).
/// Gated by CAP-O2C-DELIVERABLE.
///
/// <para>Primary link is <see cref="JobId"/> (engagement, per G-17 spike
/// — Engagement = Job on Engagement track type). <see cref="ProjectId"/>
/// is an optional rollup for Enterprise installs that run heavyweight
/// project accounting alongside engagements. <see cref="CustomerId"/>
/// is denormalized for cross-engagement reporting on a client's full
/// deliverable history.</para>
///
/// <para><see cref="FileAttachmentIds"/> is a JSON array of
/// <see cref="FileAttachment"/> ids — the actual file content lives in
/// MinIO via the FileAttachment system; the deliverable record points
/// at one or more of them. <see cref="CloudLinkExternalId"/> optionally
/// references an <see cref="EntityCloudLink"/> when the deliverable
/// folder lives on Drive / OneDrive / Dropbox.</para>
/// </summary>
public class Deliverable : BaseAuditableEntity
{
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }

    /// <summary>Primary engagement linkage (Job on Engagement track type per G-17 spike).</summary>
    public int? JobId { get; set; }

    /// <summary>Optional rollup to a heavyweight Project (Enterprise installs only).</summary>
    public int? ProjectId { get; set; }

    /// <summary>Denormalized customer for cross-engagement deliverable reporting.</summary>
    public int? CustomerId { get; set; }

    /// <summary>FK → reference_data (group: deliverable_type) — Report / Code / Design / etc.</summary>
    public int DeliverableTypeId { get; set; }

    /// <summary>One of: <c>"Draft"</c>, <c>"In Review"</c>, <c>"Approved"</c>, <c>"Delivered"</c>.</summary>
    public string Status { get; set; } = "Draft";

    public DateTimeOffset? DueDate { get; set; }
    public DateTimeOffset? DeliveredAt { get; set; }

    /// <summary>User who marked the deliverable Delivered. Null until status flips to Delivered.</summary>
    public int? DeliveredByUserId { get; set; }

    /// <summary>JSON array of FileAttachment ids — the deliverable's content lives there.</summary>
    public string? FileAttachmentIds { get; set; }

    /// <summary>Optional provider-side folder id when the deliverable folder lives on a cloud provider.</summary>
    public string? CloudLinkExternalId { get; set; }

    public Job? Job { get; set; }
    public Project? Project { get; set; }
    public Customer? Customer { get; set; }
}
