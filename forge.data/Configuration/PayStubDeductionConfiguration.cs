using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

using Forge.Core.Entities;

namespace Forge.Data.Configuration;

public class PayStubDeductionConfiguration : IEntityTypeConfiguration<PayStubDeduction>
{
    public void Configure(EntityTypeBuilder<PayStubDeduction> builder)
    {
        builder.Property(e => e.Description).HasMaxLength(200);
        builder.Property(e => e.Amount).HasPrecision(18, 2);

        builder.HasOne(e => e.PayStub)
            .WithMany(p => p.Deductions)
            .HasForeignKey(e => e.PayStubId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
