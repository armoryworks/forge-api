namespace Forge.Core.Settings;

/// <summary>
/// Phase 1m option-3 — per-integration descriptor. Wraps multiple
/// <see cref="SettingDescriptor"/> entries under one user-facing
/// "integration card" (SMTP, MinIO, Twilio, Gmail-OAuth, etc.) with
/// the rich UX metadata the existing admin integrations page renders:
/// brand logo, setup walkthrough, dev-portal signup URL, IsConfigured
/// derivation rule.
///
/// Replaces the inline integration list previously hand-rolled inside
/// the GetIntegrationSettings handler — the catalog is now the single
/// source of truth and the handler is a projection over it.
/// </summary>
public sealed record IntegrationDescriptor(
    /// <summary>Stable provider key — "smtp", "minio", "twilio",
    /// "gmail-oauth", etc. Matches the existing <c>IntegrationStatusModel.Provider</c>
    /// shape so the UI doesn't need updating.</summary>
    string Provider,
    string Name,
    string Description,
    /// <summary>Material icon name for the integration card.</summary>
    string Icon,
    /// <summary>"service" / "shipping" / "accounting" / "communications".
    /// Drives admin-page section grouping.</summary>
    string Category,
    /// <summary>Field keys that compose this integration's editable
    /// surface. Each must resolve in <see cref="SettingDescriptorCatalog"/>.</summary>
    IReadOnlyList<string> FieldKeys,
    /// <summary>The descriptor key whose presence flips IsConfigured to
    /// true. Convention: the "primary credential" key (ClientId for
    /// OAuth, ApiKey for API-key services, Host for SMTP). Null = derive
    /// from "any required field has a value".</summary>
    string? IsConfiguredCheckKey = null,
    /// <summary>Optional brand logo URL — clearbit.com/{domain} works
    /// for most. Null when the integration is generic (SMTP, local
    /// storage).</summary>
    string? LogoUrl = null,
    /// <summary>Step-by-step walkthrough an admin reads while signing
    /// up for the provider's developer portal + dropping creds into
    /// the integration card.</summary>
    IReadOnlyList<string>? SetupSteps = null,
    /// <summary>Direct link to the provider's signup / dev-portal
    /// landing page — the "I want to set this up" CTA destination.</summary>
    string? SignupUrl = null);
