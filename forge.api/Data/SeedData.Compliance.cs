using Microsoft.EntityFrameworkCore;

using Forge.Core.Entities.Compliance;
using Forge.Core.Enums;
using Forge.Data.Context;

namespace Forge.Api.Data;

public static partial class SeedData
{
    /// <summary>
    /// regulated-parts-safety C-1. Seeds the per-industry compliance profiles (inactive by
    /// default; admin activates). Effective requirements are the union of active profiles.
    /// Idempotent.
    /// </summary>
    public static async Task SeedComplianceProfilesAsync(AppDbContext db)
    {
        var profiles = new (string Industry, string Name, TraceabilityType Trace, bool Sds)[]
        {
            ("firearms", "Firearms (ATF)", TraceabilityType.Serial, false),
            ("food", "Food (FDA / FSMA)", TraceabilityType.Lot, true),
            ("medical", "Medical Devices (FDA)", TraceabilityType.Lot, true),
        };

        foreach (var p in profiles)
        {
            if (!await db.ComplianceProfiles.AnyAsync(x => x.IndustryKey == p.Industry))
            {
                db.ComplianceProfiles.Add(new ComplianceProfile
                {
                    IndustryKey = p.Industry,
                    Name = p.Name,
                    RequiredTraceabilityType = p.Trace,
                    SdsRequired = p.Sds,
                    IsActive = false,
                    IsSystem = true,
                });
            }
        }
        await db.SaveChangesAsync();
    }
}
