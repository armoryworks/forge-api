namespace Forge.Core.Enums;

/// <summary>
/// Lifecycle of a NACHA payment batch (BANK-002 Phase A — manual portal upload).
/// Release is the segregation-of-duties step: a DIFFERENT user than the batch
/// creator confirms the file was uploaded to the bank portal, which is what marks
/// the member payments as transmitted.
/// </summary>
public enum PaymentBatchStatus
{
    /// <summary>Assembled — payments selected, no file generated yet. Membership can still change.</summary>
    Draft,

    /// <summary>NACHA file generated and stored — ready for download + portal upload. Membership frozen.</summary>
    Generated,

    /// <summary>Released by a second user after portal upload — member payments are transmitted (submission accepted, NOT settled).</summary>
    Released,

    /// <summary>Cancelled before release — member payments return to the eligible pool.</summary>
    Cancelled,
}
