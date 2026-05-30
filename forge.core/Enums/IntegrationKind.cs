namespace Forge.Core.Enums;

/// <summary>
/// Categorizes a row in the Connections registry — the federated admin view
/// over every credential / connection an install has issued or accepted.
/// One value per native management surface, so the registry's "Manage"
/// deep-link can route to the right page.
/// </summary>
public enum IntegrationKind
{
    /// <summary>Unbound, read-only BI API keys (synthetic <c>BiApiClient</c>
    /// role). Managed at <c>/admin/bi-api-keys</c>.</summary>
    BiApiKey,

    /// <summary>User-bound system API keys for headless integrations.
    /// Managed at <c>/admin/system-api-keys</c>.</summary>
    SystemApiKey,

    /// <summary>EDI trading partner. Managed at <c>/admin/edi/{id}</c>.</summary>
    EdiTradingPartner,

    /// <summary>QuickBooks Online OAuth connection (singleton — at most one
    /// per install). Managed at <c>/admin/integrations</c>.</summary>
    QuickBooksOAuth,

    /// <summary>Per-user IMAP / Gmail / Outlook / Twilio communications
    /// sync. Managed in the user's account settings (or via admin user
    /// detail).</summary>
    CommunicationSync,

    /// <summary>Per-user cloud-storage link (Google Drive / OneDrive /
    /// Dropbox). Managed in the user's account settings.</summary>
    CloudStorageLink,
}
