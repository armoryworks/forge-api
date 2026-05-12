using Forge.Core.Enums;

namespace Forge.Core.Entities;

public class ContactInteraction : BaseAuditableEntity
{
    public int ContactId { get; set; }
    public int UserId { get; set; }
    public InteractionType Type { get; set; }
    public string Subject { get; set; } = string.Empty;
    public string? Body { get; set; }
    public DateTimeOffset InteractionDate { get; set; }
    public int? DurationMinutes { get; set; }

    // Navigation
    public Contact Contact { get; set; } = null!;
}
