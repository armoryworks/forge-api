using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

using Forge.Core.Entities;

namespace Forge.Data.Configuration;

public class AnnouncementTeamConfiguration : IEntityTypeConfiguration<AnnouncementTeam>
{
    public void Configure(EntityTypeBuilder<AnnouncementTeam> builder)
    {
        builder.HasKey(at => new { at.AnnouncementId, at.TeamId });

        builder.HasOne(at => at.Announcement)
            .WithMany(a => a.TargetTeams)
            .HasForeignKey(at => at.AnnouncementId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne(at => at.Team)
            .WithMany()
            .HasForeignKey(at => at.TeamId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
