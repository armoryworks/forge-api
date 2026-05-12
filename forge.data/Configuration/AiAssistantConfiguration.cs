using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

using Forge.Core.Entities;

namespace Forge.Data.Configuration;

public class AiAssistantConfiguration : IEntityTypeConfiguration<AiAssistant>
{
    public void Configure(EntityTypeBuilder<AiAssistant> builder)
    {
        builder.Property(e => e.Name).IsRequired().HasMaxLength(100);
        builder.Property(e => e.Description).HasMaxLength(500);
        builder.Property(e => e.Icon).IsRequired().HasMaxLength(50);
        builder.Property(e => e.Color).IsRequired().HasMaxLength(20);
        builder.Property(e => e.Category).IsRequired().HasMaxLength(50);
        builder.Property(e => e.SystemPrompt).IsRequired().HasMaxLength(50000);
        builder.Property(e => e.AllowedEntityTypes).HasMaxLength(2000);
        builder.Property(e => e.StarterQuestions).HasMaxLength(5000);

        builder.HasIndex(e => new { e.IsActive, e.SortOrder });
    }
}
