
namespace Forge.Core.Entities;

public class MrpException : BaseEntity
{
    public int MrpRunId { get; set; }
    public int PartId { get; set; }
    public MrpExceptionType ExceptionType { get; set; }
    public string Message { get; set; } = string.Empty;
    public string? SuggestedAction { get; set; }
    public bool IsResolved { get; set; }
    public int? ResolvedByUserId { get; set; }
    public DateTimeOffset? ResolvedAt { get; set; }
    public string? ResolutionNotes { get; set; }

    public MrpRun MrpRun { get; set; } = null!;
    public Part Part { get; set; } = null!;
}
