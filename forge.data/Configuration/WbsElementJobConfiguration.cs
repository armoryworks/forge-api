using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

using Forge.Core.Entities;

namespace Forge.Data.Configuration;

public class WbsElementJobConfiguration : IEntityTypeConfiguration<WbsElementJob>
{
    public void Configure(EntityTypeBuilder<WbsElementJob> builder)
    {
        builder.ToTable("wbs_element_jobs");
        builder.HasIndex(e => new { e.WbsElementId, e.JobId }).IsUnique();
        builder.HasIndex(e => e.JobId);
        builder.HasOne(e => e.WbsElement).WithMany().HasForeignKey(e => e.WbsElementId).OnDelete(DeleteBehavior.Cascade);
        builder.HasOne(e => e.Job).WithMany().HasForeignKey(e => e.JobId).OnDelete(DeleteBehavior.Cascade);
    }
}
