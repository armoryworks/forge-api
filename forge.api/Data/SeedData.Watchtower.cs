using Microsoft.EntityFrameworkCore;

using Forge.Core.Entities.Regulatory;
using Forge.Core.Enums;
using Forge.Data.Context;

namespace Forge.Api.Data;

public static partial class SeedData
{
    /// <summary>
    /// regulatory-watchtower (cluster B). Seeds a starter set of regulatory sources from the
    /// `regulatory-source-inventory` reference doc (backbone + domain sources). Inactive by
    /// default; admins activate. Idempotent. NB: polling requires outbound internet.
    /// </summary>
    public static async Task SeedRegulatorySourcesAsync(AppDbContext db)
    {
        var sources = new (string Name, string Body, string Domain, string Url, RegulatoryFeedType Feed, string? Industry)[]
        {
            ("Federal Register", "NARA/OFR", "cross-cutting", "https://www.federalregister.gov", RegulatoryFeedType.Api, null),
            ("Unified Agenda", "OMB/OIRA", "cross-cutting", "https://www.reginfo.gov", RegulatoryFeedType.Bulk, null),
            ("Regulations.gov", "GSA/eRulemaking", "cross-cutting", "https://www.regulations.gov", RegulatoryFeedType.Api, null),
            ("OSHA Rulemaking", "DOL-OSHA", "safety", "https://www.osha.gov/laws-regs", RegulatoryFeedType.Rss, null),
            ("EPA Rules & Regulations", "EPA", "environmental", "https://www.epa.gov/laws-regulations", RegulatoryFeedType.Email, null),
            ("ATF Rules & Regulations", "DOJ-ATF", "firearms", "https://www.atf.gov/rules-and-regulations", RegulatoryFeedType.Scrape, "firearms"),
            ("FDA Food / FSMA", "HHS-FDA", "food", "https://www.fda.gov/food", RegulatoryFeedType.Api, "food"),
            ("IRS Newsroom", "Treasury-IRS", "tax", "https://www.irs.gov/newsroom", RegulatoryFeedType.Rss, null),
            ("USCIS I-9 / E-Verify", "DHS-USCIS", "labor", "https://www.uscis.gov", RegulatoryFeedType.Email, null),
            ("CPSC", "CPSC", "product-safety", "https://www.cpsc.gov", RegulatoryFeedType.Api, null),
        };

        foreach (var s in sources)
        {
            if (!await db.RegulatorySources.AnyAsync(x => x.Name == s.Name))
            {
                db.RegulatorySources.Add(new RegulatorySource
                {
                    Name = s.Name,
                    IssuingBody = s.Body,
                    Domain = s.Domain,
                    Url = s.Url,
                    FeedType = s.Feed,
                    IndustryGate = s.Industry,
                    IsActive = false,
                });
            }
        }
        await db.SaveChangesAsync();
    }
}
