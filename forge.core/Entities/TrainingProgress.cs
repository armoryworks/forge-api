using Forge.Core.Enums;

namespace Forge.Core.Entities;

public class TrainingProgress : BaseAuditableEntity
{
    public int UserId { get; set; }
    public int ModuleId { get; set; }
    public TrainingProgressStatus Status { get; set; }
    public int? QuizScore { get; set; }
    public int? QuizAttempts { get; set; }
    public DateTimeOffset? StartedAt { get; set; }
    public DateTimeOffset? CompletedAt { get; set; }
    public int TimeSpentSeconds { get; set; }
    public string? QuizAnswersJson { get; set; }
    /// <summary>
    /// Stores the question IDs selected for the current quiz session (random subset of the pool).
    /// Cleared on retry so a fresh random selection is made on next load.
    /// </summary>
    public string? QuizSessionJson { get; set; }
    public int? WalkthroughStepReached { get; set; }

    public TrainingModule Module { get; set; } = null!;
}
