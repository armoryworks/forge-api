using Forge.Core.Entities;
using Forge.Core.Models;

namespace Forge.Core.Interfaces;

public interface IQuoteRepository
{
    Task<List<QuoteListItemModel>> GetAllAsync(int? customerId, QuoteStatus? status, CancellationToken ct);
    Task<Quote?> FindAsync(int id, CancellationToken ct);
    Task<Quote?> FindWithDetailsAsync(int id, CancellationToken ct);
    Task<string> GenerateNextQuoteNumberAsync(CancellationToken ct);
    Task AddAsync(Quote quote, CancellationToken ct);
    Task SaveChangesAsync(CancellationToken ct);
}
