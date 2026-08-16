namespace Forge.Core.Enums;

/// <summary>
/// What a party actually stated. The production gate ignores this — it only
/// asks whether an Accepted row exists for the order — but the audit trail
/// needs to distinguish "they accepted this order" from "they signed a master
/// agreement three years ago that authorizes it".
/// </summary>
public enum AttestationStatementType
{
    /// <summary>Acceptance of one Sales Order's terms. The pre-existing meaning; every legacy row is this.</summary>
    OrderAcceptance,

    /// <summary>The customer's own purchase order, supplied as their instrument of authorization.</summary>
    PurchaseOrder,

    /// <summary>A standing agreement (MSA, supply agreement) that authorizes future orders. Party-scoped — no SalesOrderId.</summary>
    MasterAgreement,

    /// <summary>Acceptance of published terms and conditions.</summary>
    TermsAccepted,

    /// <summary>Withdrawal of a prior statement. Points at what it supersedes.</summary>
    Cancellation,
}
