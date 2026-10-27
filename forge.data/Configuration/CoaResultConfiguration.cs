using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

using Forge.Core.Entities;

namespace Forge.Data.Configuration;

public class CoaResultConfiguration : IEntityTypeConfiguration<CoaResult>
{
    public void Configure(EntityTypeBuilder<CoaResult> builder)
    {
        builder.ToTable("coa_results");

        builder.Property(e => e.Characteristic).HasMaxLength(300).IsRequired();
        builder.Property(e => e.Specification).HasMaxLength(500);
        builder.Property(e => e.SpecMin).HasPrecision(18, 6);
        builder.Property(e => e.SpecMax).HasPrecision(18, 6);
        builder.Property(e => e.SpecTarget).HasPrecision(18, 6);
        builder.Property(e => e.Result).HasMaxLength(200);
        builder.Property(e => e.Unit).HasMaxLength(30);
        builder.Property(e => e.Method).HasMaxLength(200);
        builder.Property(e => e.SourceType).HasMaxLength(20).IsRequired();
    }
}
