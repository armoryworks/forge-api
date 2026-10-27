using Forge.Core.Entities;
using Forge.Core.Models;

namespace Forge.Api.Features.Inventory;

/// <summary>
/// Inbound consignment stock: vendor-owned quantity held in our bins under a <see cref="ConsignmentAgreement"/>.
/// A receipt puts it in a bin flagged with the owning vendor and carries no valuation and no journal. Consuming it
/// moves ownership to the company at the agreed price: the quantity becomes company-owned stock in the same bin,
/// the valuation store takes the receipt and, with CAP-ACCT-FULLGL, Dr INVENTORY_* / Cr CONSIGNMENT_LIABILITY posts as
/// the system. Reconciling bills the consumed, unbilled quantity to the vendor as a draft bill whose lines clear
/// CONSIGNMENT_LIABILITY when it is approved.
/// </summary>
public interface IConsignmentStockService
{
    /// <summary>Receives vendor-owned quantity into a bin (the default location when none is named).</summary>
    Task<ConsignmentTransactionResponseModel> ReceiveAsync(
        int agreementId, RecordConsignmentTransactionRequestModel request, int userId, CancellationToken ct = default);

    /// <summary>
    /// Takes ownership of consigned quantity from a named bin, or from the agreement's bins oldest first (within one
    /// location when given). Returns the consumption and the company-owned rows the quantity landed in.
    /// </summary>
    Task<(ConsignmentTransaction Transaction, IReadOnlyList<BinContent> OwnedRows)> TakeOwnershipAsync(
        int agreementId, decimal quantity, int? binContentId, int? storageLocationId, int? jobId, string? notes, int userId,
        CancellationToken ct = default);

    /// <summary>Takes ownership of part of one consigned bin row (an issue to a job straight from a consigned bin).</summary>
    Task<BinContent> TakeOwnershipOfRowAsync(BinContent consigned, decimal quantity, int? jobId, string? notes, int userId,
        CancellationToken ct = default);

    /// <summary>
    /// The vendor statement for a period: consumption not yet billed, an optional count of the consigned stock
    /// (adjusting the vendor-owned quantity, no journal), and a draft vendor bill for the consumption.
    /// </summary>
    Task<ConsignmentReconciliationResponseModel> ReconcileAsync(
        int agreementId, ReconcileConsignmentRequestModel request, int userId, CancellationToken ct = default);

    /// <summary>The agreement's transactions, newest first.</summary>
    Task<IReadOnlyList<ConsignmentTransactionResponseModel>> GetTransactionsAsync(int agreementId, CancellationToken ct = default);
}
