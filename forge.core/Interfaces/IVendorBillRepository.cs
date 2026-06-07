using Forge.Core.Entities;
using Forge.Core.Enums;
using Forge.Core.Models;

namespace Forge.Core.Interfaces;

/// <summary>⚡ ACCOUNTING BOUNDARY — VendorBill (AP) repository, AP counterpart of <c>IInvoiceRepository</c>.</summary>
public interface IVendorBillRepository
{
    Task<List<VendorBillListItemModel>> GetAllAsync(int? vendorId, VendorBillStatus? status, CancellationToken ct);
    Task<VendorBill?> FindAsync(int id, CancellationToken ct);
    Task<VendorBill?> FindWithDetailsAsync(int id, CancellationToken ct);
    /// <summary>True if a bill already exists for this vendor + vendor-invoice-number (duplicate-bill guard).</summary>
    Task<bool> ExistsForVendorInvoiceAsync(int vendorId, string vendorInvoiceNumber, CancellationToken ct);
    Task<string> GenerateNextBillNumberAsync(CancellationToken ct);
    Task AddAsync(VendorBill bill, CancellationToken ct);
    Task SaveChangesAsync(CancellationToken ct);
}
