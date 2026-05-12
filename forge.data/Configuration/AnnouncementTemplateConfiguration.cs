using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

using Forge.Core.Entities;

namespace Forge.Data.Configuration;

public class AnnouncementTemplateConfiguration : IEntityTypeConfiguration<AnnouncementTemplate>
{
    public void Configure(EntityTypeBuilder<AnnouncementTemplate> builder)
    {
        builder.Property(t => t.Name).HasMaxLength(200);
        builder.Property(t => t.Content).HasMaxLength(5000);

        builder.Property(t => t.DefaultSeverity)
            .HasConversion<string>()
            .HasMaxLength(20);

        builder.Property(t => t.DefaultScope)
            .HasConversion<string>()
            .HasMaxLength(20);
    }
}
