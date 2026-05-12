using Forge.Core.Entities;
using Forge.Core.Models;

namespace Forge.Core.Interfaces;

public interface IConsignmentService
{
    Task<ConsignmentAgreement> CreateAgreementAsync(CreateConsignmentAgreementRequestModel request, CancellationToken ct);
    Task<ConsignmentAgreement> UpdateAgreementAsync(int agreementId, UpdateConsignmentAgreementRequestModel request, CancellationToken ct);
    Task RecordConsumptionAsync(int agreementId, decimal quantity, string? notes, CancellationToken ct);
    Task RecordReceiptAsync(int agreementId, decimal quantity, string? notes, CancellationToken ct);
    Task<ConsignmentReconciliationResponseModel> ReconcileAsync(int agreementId, decimal physicalCount, CancellationToken ct);
    Task<IReadOnlyList<ConsignmentAgreement>> GetAgreementsByVendorAsync(int vendorId, CancellationToken ct);
    Task<IReadOnlyList<ConsignmentAgreement>> GetAgreementsByCustomerAsync(int customerId, CancellationToken ct);
    Task<ConsignmentStockSummaryResponseModel> GetStockSummaryAsync(int? vendorId, int? customerId, CancellationToken ct);
}
