namespace Forge.Api.Features.Presets;

/// <summary>
/// Phase 4 Phase-G — Per-preset display metadata used by the preset browser.
/// "Recommended for" tags are short copy points the catalog browser uses to
/// help admins eyeball-match a preset to their business shape without
/// reading the full TargetProfile prose.
/// </summary>
public static class PresetMetadata
{
    public static IReadOnlyList<string> GetRecommendedForTags(string presetId) => presetId switch
    {
        "PRESET-01" => ["1-3 people", "Owner-operator", "Single product line"],
        "PRESET-02" => ["4-25 people", "External accounting", "Mixed work"],
        "PRESET-03" => ["Distribution", "5-50 people", "No production", "Drop-ship"],
        "PRESET-04" => ["25-200 people", "Production manager", "ISO 9001 baseline"],
        "PRESET-05" => ["Regulated industry", "ISO 13485 / AS9100 / IATF / FDA", "Full QC stack"],
        "PRESET-06" => ["50-500 people", "Multi-site (2+ plants)", "EDI", "MRP/MPS"],
        "PRESET-07" => ["200+ people", "Multi-currency", "CTO/ETO", "Machine connect"],
        "PRESET-08" => ["5-50 people", "Consulting / agency", "Agile task types", "Billable hours"],
        "PRESET-09" => ["10-100 people", "Make + service", "Hybrid + agile tasks"],
        "PRESET-CUSTOM" => ["Hand-pick capabilities", "Catalog defaults"],
        _ => [],
    };
}
