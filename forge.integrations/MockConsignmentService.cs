using Microsoft.Extensions.Logging;

using Forge.Core.Entities;
using Forge.Core.Interfaces;
using Forge.Core.Models;

namespace Forge.Integrations;

public class MockConsignmentService(ILogger<MockConsignmentService> logger) : IConsignmentService
{
    public Task<ConsignmentAgreement> CreateAgreementAsync(CreateConsignmentAgreementRequestModel request, CancellationToken ct)
    {
        logger.LogInformation("[MockConsignment] CreateAgreement for Part {PartId}, Direction={Direction}", request.PartId, request.Direction);
        var agreement = new ConsignmentAgreement
        {
            Id = 1,
            Direction = request.Direction,
            VendorId = request.VendorId,
            CustomerId = request.CustomerId,
            PartId = request.PartId,
            AgreedUnitPrice = request.AgreedUnitPrice,
            MinStockQuantity = request.MinStockQuantity,
            MaxStockQuantity = request.MaxStockQuantity,
            EffectiveFrom = request.EffectiveFrom,
            EffectiveTo = request.EffectiveTo,
            InvoiceOnConsumption = request.InvoiceOnConsumption,
            Terms = request.Terms,
            ReconciliationFrequencyDays = request.ReconciliationFrequencyDays,
            Status = ConsignmentAgreementStatus.Active,
        };
        return Task.FromResult(agreement);
    }

    public Task<ConsignmentAgreement> UpdateAgreementAsync(int agreementId, UpdateConsignmentAgreementRequestModel request, CancellationToken ct)
    {
        logger.LogInformation("[MockConsignment] UpdateAgreement {AgreementId}", agreementId);
        return Task.FromResult(new ConsignmentAgreement { Id = agreementId });
    }

    public Task RecordConsumptionAsync(int agreementId, decimal quantity, string? notes, CancellationToken ct)
    {
        logger.LogInformation("[MockConsignment] RecordConsumption on Agreement {AgreementId}, Qty={Quantity}", agreementId, quantity);
        return Task.CompletedTask;
    }

    public Task RecordReceiptAsync(int agreementId, decimal quantity, string? notes, CancellationToken ct)
    {
        logger.LogInformation("[MockConsignment] RecordReceipt on Agreement {AgreementId}, Qty={Quantity}", agreementId, quantity);
        return Task.CompletedTask;
    }

    public Task<ConsignmentReconciliationResponseModel> ReconcileAsync(int agreementId, decimal physicalCount, CancellationToken ct)
    {
        logger.LogInformation("[MockConsignment] Reconcile Agreement {AgreementId}, PhysicalCount={PhysicalCount}", agreementId, physicalCount);
        return Task.FromResult(new ConsignmentReconciliationResponseModel
        {
            AgreementId = agreementId,
            BookQuantity = physicalCount,
            PhysicalQuantity = physicalCount,
            Variance = 0,
        });
    }

    public Task<IReadOnlyList<ConsignmentAgreement>> GetAgreementsByVendorAsync(int vendorId, CancellationToken ct)
    {
        logger.LogInformation("[MockConsignment] GetAgreementsByVendor {VendorId}", vendorId);
        return Task.FromResult<IReadOnlyList<ConsignmentAgreement>>([]);
    }

    public Task<IReadOnlyList<ConsignmentAgreement>> GetAgreementsByCustomerAsync(int customerId, CancellationToken ct)
    {
        logger.LogInformation("[MockConsignment] GetAgreementsByCustomer {CustomerId}", customerId);
        return Task.FromResult<IReadOnlyList<ConsignmentAgreement>>([]);
    }

    public Task<ConsignmentStockSummaryResponseModel> GetStockSummaryAsync(int? vendorId, int? customerId, CancellationToken ct)
    {
        logger.LogInformation("[MockConsignment] GetStockSummary VendorId={VendorId}, CustomerId={CustomerId}", vendorId, customerId);
        return Task.FromResult(new ConsignmentStockSummaryResponseModel
        {
            TotalAgreements = 0,
            ActiveAgreements = 0,
            TotalConsignedValue = 0,
            ByOwner = [],
        });
    }
}
