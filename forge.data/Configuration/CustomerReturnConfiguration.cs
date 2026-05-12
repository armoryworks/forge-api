using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

using Forge.Core.Entities;

namespace Forge.Data.Configuration;

public class CustomerReturnConfiguration : IEntityTypeConfiguration<CustomerReturn>
{
    public void Configure(EntityTypeBuilder<CustomerReturn> builder)
    {
        builder.Property(e => e.ReturnNumber).HasMaxLength(50);
        builder.Property(e => e.Reason).HasMaxLength(1000);
        builder.Property(e => e.Notes).HasMaxLength(2000);
        builder.Property(e => e.InspectionNotes).HasMaxLength(2000);

        builder.HasIndex(e => e.ReturnNumber).IsUnique();
        builder.HasIndex(e => e.Status);

        builder.HasOne(e => e.Customer)
            .WithMany()
            .HasForeignKey(e => e.CustomerId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(e => e.OriginalJob)
            .WithMany()
            .HasForeignKey(e => e.OriginalJobId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(e => e.ReworkJob)
            .WithMany()
            .HasForeignKey(e => e.ReworkJobId)
            .OnDelete(DeleteBehavior.SetNull);
    }
}
