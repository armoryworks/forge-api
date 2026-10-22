using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

using Forge.Core.Entities;

namespace Forge.Data.Configuration;

public class OperationPredecessorConfiguration : IEntityTypeConfiguration<OperationPredecessor>
{
    public void Configure(EntityTypeBuilder<OperationPredecessor> builder)
    {
        builder.HasKey(p => new { p.OperationId, p.PredecessorOperationId });

        builder.HasIndex(p => p.PredecessorOperationId);

        builder.HasOne(p => p.Operation)
            .WithMany(o => o.Predecessors)
            .HasForeignKey(p => p.OperationId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne(p => p.PredecessorOperation)
            .WithMany()
            .HasForeignKey(p => p.PredecessorOperationId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
