using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Forge.Core.Entities;

namespace Forge.Data.Configuration;

public class ContactInteractionConfiguration : IEntityTypeConfiguration<ContactInteraction>
{
    public void Configure(EntityTypeBuilder<ContactInteraction> builder)
    {
        builder.HasOne(e => e.Contact)
            .WithMany()
            .HasForeignKey(e => e.ContactId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasIndex(e => e.ContactId);
        builder.HasIndex(e => e.UserId);

        builder.Property(e => e.Subject).HasMaxLength(200).IsRequired();
        builder.Property(e => e.Body).HasMaxLength(4000);
        builder.Property(e => e.Type).HasConversion<string>().HasMaxLength(20);
    }
}
