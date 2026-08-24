using System.ComponentModel.DataAnnotations;

namespace Forge.Core.Entities;

/// <summary>
/// A jurisdiction's minimum hourly wage, effective-dated. Null state = the
/// federal floor. Seeded as reference data; admin-editable so installs keep
/// their jurisdictions current (assistance, not legal advice — verify with the
/// state DOL).
/// </summary>
public class MinimumWageRate : BaseAuditableEntity
{
    /// <summary>Two-letter state code; null = the federal default rate.</summary>
    [MaxLength(2)]
    public string? StateCode { get; set; }

    public decimal RatePerHour { get; set; }

    public DateOnly EffectiveFrom { get; set; }

    public DateOnly? EffectiveTo { get; set; }

    [MaxLength(256)]
    public string? Description { get; set; }
}
