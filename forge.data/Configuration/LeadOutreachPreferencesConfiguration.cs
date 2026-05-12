using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Forge.Core.Entities;

namespace Forge.Data.Configuration;

public class LeadOutreachPreferencesConfiguration : IEntityTypeConfiguration<LeadOutreachPreferences>
{
    public void Configure(EntityTypeBuilder<LeadOutreachPreferences> builder)
    {
        builder.Ignore(e => e.IsDeleted);

        builder.Property(e => e.EmailOptOutSource).HasMaxLength(200);
        builder.Property(e => e.CallOptOutSource).HasMaxLength(200);
        builder.Property(e => e.SmsOptOutSource).HasMaxLength(200);
        builder.Property(e => e.CooldownReasonCode).HasMaxLength(100);
        builder.Property(e => e.CooldownNotes).HasMaxLength(500);

        // 0..1:1 — exactly one preferences row per non-deleted lead.
        builder.HasIndex(e => e.LeadId)
            .IsUnique()
            .HasFilter(@"deleted_at IS NULL");

        builder.HasIndex(e => e.CooldownUntil);

        builder.HasOne(e => e.Lead)
            .WithOne()
            .HasForeignKey<LeadOutreachPreferences>(e => e.LeadId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
