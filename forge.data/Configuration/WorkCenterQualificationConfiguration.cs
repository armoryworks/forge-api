using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

using Forge.Core.Entities;
using Forge.Data.Context;

namespace Forge.Data.Configuration;

public class WorkCenterQualificationConfiguration : IEntityTypeConfiguration<WorkCenterQualification>
{
    public void Configure(EntityTypeBuilder<WorkCenterQualification> builder)
    {
        builder.HasKey(q => new { q.UserId, q.WorkCenterId });

        builder.Property(q => q.Notes).HasMaxLength(500);

        builder.HasIndex(q => q.WorkCenterId);
        builder.HasIndex(q => q.QualifiedById);

        builder.HasOne(q => q.WorkCenter)
            .WithMany()
            .HasForeignKey(q => q.WorkCenterId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne<ApplicationUser>()
            .WithMany()
            .HasForeignKey(q => q.UserId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne<ApplicationUser>()
            .WithMany()
            .HasForeignKey(q => q.QualifiedById)
            .OnDelete(DeleteBehavior.SetNull);
    }
}
