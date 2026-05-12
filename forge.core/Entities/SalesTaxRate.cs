namespace Forge.Core.Entities;

public class SalesTaxRate : BaseAuditableEntity
{
    public string Name { get; set; } = string.Empty;
    public string Code { get; set; } = string.Empty;
    /// <summary>
    /// 2-letter US state code (e.g. "CA", "TX"). Null = general/default rate.
    /// Used for automatic lookup based on customer's ship-to state.
    /// </summary>
    public string? StateCode { get; set; }
    /// <summary>
    /// The effective combined rate (state + typical local), stored as a decimal fraction.
    /// E.g. 0.0725 = 7.25%. Admins should set this to the actual combined rate
    /// for their nexus jurisdictions. Local rates vary by city/county — see state tax authority.
    /// </summary>
    public decimal Rate { get; set; }
    /// <summary>When this rate takes effect (UTC). Used to schedule future rate changes.</summary>
    public DateTimeOffset EffectiveFrom { get; set; }
    /// <summary>When this rate expires (UTC). Null = currently active.</summary>
    public DateTimeOffset? EffectiveTo { get; set; }
    public bool IsDefault { get; set; }
    public bool IsActive { get; set; } = true;
    public string? Description { get; set; }

    /// <summary>
    /// Marks this rate row as an exemption marker — e.g. a 0% row used to
    /// flag a jurisdiction or reason where sales tax does not apply. Local
    /// concern; orthogonal to <see cref="Rate"/> so a 0% non-exempt row
    /// (rate temporarily zeroed) and a 0% exempt row (always exempt) can
    /// coexist. Phase 3 F5.
    /// </summary>
    public bool ExemptFlag { get; set; }

    /// <summary>
    /// External-accounting GL posting account this tax should map to when the
    /// accounting-sync integration is configured. Optional — populated by the
    /// provider's tax-account mapping flow. Phase 3 F5.
    /// </summary>
    public string? GlPostingAccount { get; set; }
}
