using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Forge.Core.Entities;

namespace Forge.Data.Configuration;

public class OutreachCampaignConfiguration : IEntityTypeConfiguration<OutreachCampaign>
{
    public void Configure(EntityTypeBuilder<OutreachCampaign> builder)
    {
        builder.Ignore(e => e.IsDeleted);

        builder.Property(e => e.Name).HasMaxLength(200).IsRequired();
        builder.Property(e => e.Description).HasMaxLength(1000);
        builder.Property(e => e.Strategy).HasConversion<string>().HasMaxLength(50);

        builder.HasIndex(e => e.IsActive);
        builder.HasIndex(e => e.OwnerUserId);
    }
}
