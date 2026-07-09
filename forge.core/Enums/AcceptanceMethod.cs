namespace Forge.Core.Enums;

/// <summary>
/// How a Sales Order's customer acceptance was captured. The production gate is method-agnostic —
/// it only cares that an Accepted record exists — so new channels add a value here without touching
/// the gate. The first four are staff-recorded offline channels (one capture flow); the rest are
/// self-service / automated channels.
/// </summary>
public enum AcceptanceMethod
{
    /// <summary>Staff uploaded a signed document.</summary>
    ManualUpload,
    /// <summary>Signed document received by fax, scanned and attached.</summary>
    Fax,
    /// <summary>Signed document received by email and attached.</summary>
    Email,
    /// <summary>Verbal / phone acceptance recorded by staff (weakest — no document).</summary>
    Verbal,
    /// <summary>Carried over from an online quote acceptance on quote→order conversion.</summary>
    QuotePortal,
    /// <summary>Customer accepted via the Forge public accept portal (token + proven second key).</summary>
    PublicPortal,
    /// <summary>Customer signed via an e-signature provider (DocuSeal / DocuSign / …).</summary>
    ESignature,
    /// <summary>Recorded by the customer's own system through the authenticated acceptance API.</summary>
    ExternalSystem,
}
