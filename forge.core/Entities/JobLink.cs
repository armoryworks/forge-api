
namespace Forge.Core.Entities;

public class JobLink : BaseEntity
{
    public int SourceJobId { get; set; }
    public int TargetJobId { get; set; }
    public JobLinkType LinkType { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;

    public Job SourceJob { get; set; } = null!;
    public Job TargetJob { get; set; } = null!;
}
