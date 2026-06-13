using Forge.Core.Entities;
using Forge.Core.Models;

namespace Forge.Core.Interfaces;

/// <summary>⚡ ACCOUNTING BOUNDARY — VendorPayment (AP) repository, AP counterpart of <c>IPaymentRepository</c>.</summary>
public interface IVendorPaymentRepository
{
    Task<List<VendorPaymentListItemModel>> GetAllAsync(int? vendorId, CancellationToken ct);
    Task<VendorPayment?> FindAsync(int id, CancellationToken ct);
    Task<VendorPayment?> FindWithDetailsAsync(int id, CancellationToken ct);
    /// <summary>Latest bank transmission for the payment (highest Id), or null when never transmitted.</summary>
    Task<PaymentTransmission?> FindLatestTransmissionAsync(int paymentId, CancellationToken ct);
    Task<string> GenerateNextVendorPaymentNumberAsync(CancellationToken ct);
    Task AddAsync(VendorPayment payment, CancellationToken ct);
    Task SaveChangesAsync(CancellationToken ct);
}
