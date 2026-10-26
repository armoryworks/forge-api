using Forge.Core.Enums;

namespace Forge.Core.Models;

/// <summary>
/// Resolves a customer return. <c>Disposition</c> null keeps the old behaviour (status only). <c>Quantity</c> defaults
/// to the returned quantity; <c>CreditAmount</c> is the goods credit before tax (default: quantity at the invoiced
/// price); <c>RestockingFeePercent</c> defaults to the <c>rma.restocking_fee_percent</c> setting.
/// </summary>
public record ResolveCustomerReturnRequestModel(
    string? InspectionNotes,
    ReturnDisposition? Disposition = null,
    decimal? Quantity = null,
    int? RestockLocationId = null,
    string? LotNumber = null,
    bool IssueCredit = true,
    decimal? CreditAmount = null,
    decimal? RestockingFeePercent = null);
