using Forge.Core.Entities;
using Forge.Core.Enums;
using Forge.Core.Models;

namespace Forge.Core.Interfaces;

public interface IQuoteRepository
{
    Task<List<QuoteListItemModel>> GetAllAsync(int? customerId, QuoteStatus? status, CancellationToken ct);
    Task<Quote?> FindAsync(int id, CancellationToken ct);
    Task<Quote?> FindWithDetailsAsync(int id, CancellationToken ct);
    Task<string> GenerateNextQuoteNumberAsync(CancellationToken ct);

    /// <summary>
    /// True when <paramref name="number"/> is already used by another quote.
    /// Excludes <paramref name="excludeId"/> so a quote can keep its own number on
    /// update. Estimates carry a null number, so they never match a non-null value.
    /// Mirrors <c>IPartRepository.PartNumberExistsAsync</c>.
    /// </summary>
    Task<bool> QuoteNumberExistsAsync(string number, int? excludeId, CancellationToken ct);
    Task AddAsync(Quote quote, CancellationToken ct);
    Task SaveChangesAsync(CancellationToken ct);
}
