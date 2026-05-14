using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Forge.Core.Entities;

namespace Forge.Identity.Configuration;

public class ApplicationUserConfiguration : IEntityTypeConfiguration<ApplicationUser>
{
    public void Configure(EntityTypeBuilder<ApplicationUser> builder)
    {
        // Phase 3 H2 / WU-12: IActiveAware contract member — derived from IsActive.
        builder.Ignore(e => e.IsActiveForNewTransactions);

        builder.HasIndex(e => e.WorkLocationId);

        builder.HasOne(e => e.WorkLocation)
            .WithMany()
            .HasForeignKey(e => e.WorkLocationId)
            .OnDelete(DeleteBehavior.SetNull);

        // Phase 3 / WU-06 / C1 — optional rollup role template assignment.
        builder.HasIndex(e => e.RoleTemplateId);

        builder.HasOne(e => e.RoleTemplate)
            .WithMany()
            .HasForeignKey(e => e.RoleTemplateId)
            .OnDelete(DeleteBehavior.SetNull);
    }
}
