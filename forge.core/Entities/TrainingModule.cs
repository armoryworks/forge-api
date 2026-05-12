using Forge.Core.Enums;

namespace Forge.Core.Entities;

public class TrainingModule : BaseAuditableEntity
{
    public string Title { get; set; } = string.Empty;
    public string Slug { get; set; } = string.Empty;
    public string Summary { get; set; } = string.Empty;
    public TrainingContentType ContentType { get; set; }
    public string ContentJson { get; set; } = "{}";
    public string? CoverImageUrl { get; set; }
    public int EstimatedMinutes { get; set; }
    public string? Tags { get; set; }
    public string? AppRoutes { get; set; }
    public bool IsPublished { get; set; } = false;
    public bool IsOnboardingRequired { get; set; } = false;
    public int SortOrder { get; set; }
    public int? CreatedByUserId { get; set; }

    public ICollection<TrainingPathModule> PathModules { get; set; } = [];
    public ICollection<TrainingProgress> ProgressRecords { get; set; } = [];
}
