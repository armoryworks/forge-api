namespace Forge.Core.Entities;

/// <summary>
/// Key-value store for install-wide settings: company profile fields
/// (`company.*`), accounting credentials, integration tokens. Promoted to
/// <see cref="BaseAuditableEntity"/> so the row's lifecycle timestamps
/// (created/updated/deleted) are populated automatically by
/// <c>AppDbContext.SetTimestamps()</c>. The Integrations registry surfaces
/// those timestamps as the "connected at" / "last updated" columns for
/// rows backed by SystemSetting (e.g. the QuickBooks OAuth row).
/// </summary>
public class SystemSetting : BaseAuditableEntity
{
    public string Key { get; set; } = string.Empty;
    public string Value { get; set; } = string.Empty;
    public string? Description { get; set; }
}
