using Microsoft.EntityFrameworkCore;
using Forge.Core.Entities;
using Forge.Core.Interfaces;
using Forge.Core.Models;
using Forge.Data.Context;

namespace Forge.Data.Repositories;

public class QuoteRepository(AppDbContext db) : IQuoteRepository
{
    public async Task<List<QuoteListItemModel>> GetAllAsync(
        int? customerId, QuoteStatus? status, CancellationToken ct)
    {
        var query = db.Quotes
            .Include(q => q.Customer)
            .Include(q => q.Lines)
            .Where(q => q.Type == QuoteType.Quote)
            .AsQueryable();

        if (customerId.HasValue)
            query = query.Where(q => q.CustomerId == customerId.Value);

        if (status.HasValue)
            query = query.Where(q => q.Status == status.Value);

        return await query
            .OrderByDescending(q => q.CreatedAt)
            .Select(q => new QuoteListItemModel(
                q.Id,
                q.QuoteNumber ?? string.Empty,
                q.CustomerId,
                q.Customer.Name,
                q.Status.ToString(),
                q.Lines.Count,
                q.Lines.Sum(l => l.Quantity * l.UnitPrice),
                q.ExpirationDate,
                q.CreatedAt))
            .ToListAsync(ct);
    }

    public async Task<Quote?> FindAsync(int id, CancellationToken ct)
    {
        return await db.Quotes.FirstOrDefaultAsync(q => q.Id == id && q.Type == QuoteType.Quote, ct);
    }

    public async Task<Quote?> FindWithDetailsAsync(int id, CancellationToken ct)
    {
        return await db.Quotes
            .Include(q => q.Customer)
            .Include(q => q.Lines)
                .ThenInclude(l => l.Part)
            .Include(q => q.SalesOrder)
            .Include(q => q.SourceEstimate)
            .FirstOrDefaultAsync(q => q.Id == id && q.Type == QuoteType.Quote, ct);
    }

    public async Task<string> GenerateNextQuoteNumberAsync(CancellationToken ct)
    {
        var last = await db.Quotes
            .IgnoreQueryFilters()
            .Where(q => q.Type == QuoteType.Quote && q.QuoteNumber != null)
            .OrderByDescending(q => q.Id)
            .Select(q => q.QuoteNumber)
            .FirstOrDefaultAsync(ct);

        if (last != null && last.StartsWith("QT-") && int.TryParse(last[3..], out var lastNum))
            return $"QT-{lastNum + 1:D5}";

        return "QT-00001";
    }

    public async Task AddAsync(Quote quote, CancellationToken ct)
    {
        await db.Quotes.AddAsync(quote, ct);
    }

    public async Task SaveChangesAsync(CancellationToken ct)
    {
        await db.SaveChangesAsync(ct);
    }
}
