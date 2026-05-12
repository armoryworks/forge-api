using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

using Forge.Core.Entities;

namespace Forge.Data.Configuration;

public class ReservationConfiguration : IEntityTypeConfiguration<Reservation>
{
    public void Configure(EntityTypeBuilder<Reservation> builder)
    {
        builder.Ignore(e => e.IsDeleted);

        builder.Property(e => e.Quantity).HasPrecision(18, 4);
        builder.Property(e => e.Notes).HasMaxLength(500);

        builder.HasIndex(e => e.PartId);
        builder.HasOne(e => e.Part)
            .WithMany()
            .HasForeignKey(e => e.PartId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(e => e.BinContentId);
        builder.HasOne(e => e.BinContent)
            .WithMany(b => b.Reservations)
            .HasForeignKey(e => e.BinContentId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(e => e.JobId);
        builder.HasOne(e => e.Job)
            .WithMany()
            .HasForeignKey(e => e.JobId)
            .OnDelete(DeleteBehavior.SetNull);

        builder.HasIndex(e => e.SalesOrderLineId);
        builder.HasOne(e => e.SalesOrderLine)
            .WithMany()
            .HasForeignKey(e => e.SalesOrderLineId)
            .OnDelete(DeleteBehavior.SetNull);
    }
}
