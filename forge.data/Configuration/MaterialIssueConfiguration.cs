using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

using Forge.Core.Entities;

namespace Forge.Data.Configuration;

public class MaterialIssueConfiguration : IEntityTypeConfiguration<MaterialIssue>
{
    public void Configure(EntityTypeBuilder<MaterialIssue> builder)
    {
        builder.Ignore(e => e.IsDeleted);
        builder.Ignore(e => e.TotalCost);

        builder.Property(e => e.Quantity).HasPrecision(18, 4);
        builder.Property(e => e.UnitCost).HasPrecision(18, 4);
        builder.Property(e => e.LotNumber).HasMaxLength(100);
        builder.Property(e => e.Notes).HasMaxLength(2000);

        builder.HasIndex(e => e.JobId);
        builder.HasIndex(e => e.PartId);
        builder.HasIndex(e => e.OperationId);
        builder.HasIndex(e => e.IssuedById);

        builder.HasOne(e => e.Job)
            .WithMany(j => j.MaterialIssues)
            .HasForeignKey(e => e.JobId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(e => e.Part)
            .WithMany()
            .HasForeignKey(e => e.PartId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(e => e.Operation)
            .WithMany()
            .HasForeignKey(e => e.OperationId)
            .OnDelete(DeleteBehavior.SetNull);

        builder.HasOne<Forge.Data.Context.ApplicationUser>()
            .WithMany()
            .HasForeignKey(e => e.IssuedById)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(e => e.BinContent)
            .WithMany()
            .HasForeignKey(e => e.BinContentId)
            .OnDelete(DeleteBehavior.SetNull);

        builder.HasOne(e => e.StorageLocation)
            .WithMany()
            .HasForeignKey(e => e.StorageLocationId)
            .OnDelete(DeleteBehavior.SetNull);
    }
}
