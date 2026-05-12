using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Forge.Core.Entities;

namespace Forge.Data.Configuration;

public class MrpExceptionConfiguration : IEntityTypeConfiguration<MrpException>
{
    public void Configure(EntityTypeBuilder<MrpException> builder)
    {
        builder.HasIndex(e => e.MrpRunId);
        builder.HasIndex(e => e.PartId);

        builder.Property(e => e.Message).HasMaxLength(1000);
        builder.Property(e => e.SuggestedAction).HasMaxLength(1000);
        builder.Property(e => e.ResolutionNotes).HasMaxLength(2000);

        builder.HasOne(e => e.Part)
            .WithMany()
            .HasForeignKey(e => e.PartId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(e => e.ResolvedByUserId);
    }
}
