using Forge.Core.Entities;
using Forge.Core.Models;

namespace Forge.Core.Interfaces;

public interface IPaymentRepository
{
    /// <summary>
    /// Legacy (non-paged) list. Kept for any caller that still needs the full
    /// flat array. New work should call <see cref="GetPagedAsync"/>.
    /// </summary>
    Task<List<PaymentListItemModel>> GetAllAsync(int? customerId, CancellationToken ct);

    /// <summary>
    /// Paged list per the Phase 3 F7-broad / WU-22 standard contract. Returns
    /// the slice + the total matching count for pagination UI.
    /// </summary>
    Task<PagedResponse<PaymentListItemModel>> GetPagedAsync(PaymentListQuery query, CancellationToken ct);

    Task<Payment?> FindAsync(int id, CancellationToken ct);
    Task<Payment?> FindWithDetailsAsync(int id, CancellationToken ct);
    Task<string> GenerateNextPaymentNumberAsync(CancellationToken ct);

    /// <summary>True when <paramref name="number"/> is already used by another payment (optionally excluding one id).</summary>
    Task<bool> PaymentNumberExistsAsync(string number, int? excludeId, CancellationToken ct);

    Task AddAsync(Payment payment, CancellationToken ct);
    Task SaveChangesAsync(CancellationToken ct);
}
