using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Forge.Core.Entities;

namespace Forge.Data.Configuration;

public class CompanyLocationConfiguration : IEntityTypeConfiguration<CompanyLocation>
{
    public void Configure(EntityTypeBuilder<CompanyLocation> builder)
    {
        builder.Property(e => e.Name).HasMaxLength(100);
        builder.Property(e => e.Line1).HasMaxLength(200);
        builder.Property(e => e.Line2).HasMaxLength(200);
        builder.Property(e => e.City).HasMaxLength(100);
        builder.Property(e => e.State).HasMaxLength(50);
        builder.Property(e => e.PostalCode).HasMaxLength(20);
        builder.Property(e => e.Country).HasMaxLength(10);
        builder.Property(e => e.Phone).HasMaxLength(20);

        builder.HasIndex(e => e.State);
        builder.HasIndex(e => e.IsDefault)
            .HasFilter("is_default = true")
            .IsUnique();

        // SetNull on calendar delete — losing the override falls back to
        // the tenant default. Never cascade a calendar delete into the
        // location itself.
        builder.HasOne(e => e.WorkingCalendar)
            .WithMany()
            .HasForeignKey(e => e.WorkingCalendarId)
            .OnDelete(DeleteBehavior.SetNull);
    }
}
