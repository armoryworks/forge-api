namespace Forge.Core.Entities;

public class CompanyLocation : BaseAuditableEntity
{
    public string Name { get; set; } = string.Empty;
    public string Line1 { get; set; } = string.Empty;
    public string? Line2 { get; set; }
    public string City { get; set; } = string.Empty;
    public string State { get; set; } = string.Empty;
    public string PostalCode { get; set; } = string.Empty;
    public string Country { get; set; } = "US";
    public string? Phone { get; set; }
    public bool IsDefault { get; set; }
    public bool IsActive { get; set; } = true;

    /// <summary>
    /// Optional override of the tenant default WorkingCalendar (PR 1 of the
    /// bought-parts/landed-cost effort). When null, business-day calculations
    /// for this location resolve to the tenant default. Lets a multi-site
    /// install observe Mexican holidays at the Mexican plant and US holidays
    /// at the US plant without separate configs.
    /// </summary>
    public int? WorkingCalendarId { get; set; }
    public WorkingCalendar? WorkingCalendar { get; set; }
}
