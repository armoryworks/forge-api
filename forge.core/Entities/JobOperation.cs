using Forge.Core.Enums;

namespace Forge.Core.Entities;

public class JobOperation : BaseAuditableEntity, IConcurrencyVersioned
{
    /// <summary>Optimistic-locking version. See IConcurrencyVersioned.</summary>
    public uint Version { get; set; }

    public int JobId { get; set; }
    public int? OperationId { get; set; }
    public int StepNumber { get; set; }
    public string Title { get; set; } = string.Empty;
    public JobOperationStatus Status { get; set; } = JobOperationStatus.NotStarted;
    public decimal CompletedQuantity { get; set; }
    public decimal ScrapQuantity { get; set; }
    public decimal EstSetupMinutes { get; set; }
    public decimal EstRunMinutesEach { get; set; }
    public decimal EstRunMinutesLot { get; set; }
    public DateTimeOffset? StartedAt { get; set; }
    public DateTimeOffset? CompletedAt { get; set; }
    public int? CompletedById { get; set; }

    public Job Job { get; set; } = null!;
    public Operation? Operation { get; set; }
}
