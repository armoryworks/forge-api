using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Forge.Core.Entities;

namespace Forge.Data.Configuration;

public class TrainingPathModuleConfiguration : IEntityTypeConfiguration<TrainingPathModule>
{
    public void Configure(EntityTypeBuilder<TrainingPathModule> builder)
    {
        builder.Ignore(e => e.IsDeleted);

        builder.HasIndex(e => new { e.PathId, e.Position });
        builder.HasIndex(e => e.ModuleId);

        builder.HasOne(pm => pm.Module)
            .WithMany(m => m.PathModules)
            .HasForeignKey(pm => pm.ModuleId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
