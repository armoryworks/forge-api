using Forge.Core.Enums;

namespace Forge.Core.Models;

/// <summary>
/// A draft vendor credit or debit memo. With <c>VendorBillId</c> it takes the bill's vendor, currency and booking rate;
/// otherwise the requested currency (rate on file when none is sent).
/// </summary>
public record CreateVendorCreditMemoRequestModel(
    int VendorId,
    VendorCreditMemoSourceType SourceType,
    string Reason,
    List<VendorCreditMemoLineRequestModel> Lines,
    VendorCreditMemoKind Kind = VendorCreditMemoKind.VendorCredit,
    string? VendorReference = null,
    int? VendorBillId = null,
    int? CurrencyId = null,
    decimal? FxRate = null,
    DateTimeOffset? MemoDate = null,
    decimal? TaxAmount = null,
    string? Notes = null);

/// <summary>Edits a draft memo's header and replaces its lines.</summary>
public record UpdateVendorCreditMemoRequestModel(
    VendorCreditMemoSourceType SourceType,
    string Reason,
    DateTimeOffset MemoDate,
    List<VendorCreditMemoLineRequestModel> Lines,
    VendorCreditMemoKind Kind = VendorCreditMemoKind.VendorCredit,
    string? VendorReference = null,
    decimal? TaxAmount = null,
    string? Notes = null);

/// <summary>
/// One memo line. A line naming a bill line takes its part, description and account when left blank and is capped at
/// the billed quantity and unit price.
/// </summary>
public record VendorCreditMemoLineRequestModel(
    decimal Quantity,
    decimal UnitPrice,
    int? VendorBillLineId = null,
    int? PartId = null,
    string? Description = null,
    string? AccountDeterminationKey = null,
    int? GlAccountId = null,
    int? VendorReturnLineId = null);

/// <summary>Applies part of a memo to an open bill of the same vendor and currency.</summary>
public record ApplyVendorCreditMemoRequestModel(int VendorBillId, decimal Amount);
