using Microsoft.EntityFrameworkCore;
using Forge.Core.Entities;
using Forge.Core.Enums;
using Forge.Core.Interfaces;
using Forge.Core.Models;
using Forge.Core.Models.Accounting;
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

        var rows = await query
            .OrderByDescending(q => q.CreatedAt)
            .Select(q => new
            {
                q.Id,
                QuoteNumber = q.QuoteNumber ?? string.Empty,
                q.CustomerId,
                CustomerName = q.Customer.Name,
                q.Status,
                LineCount = q.Lines.Count,
                Subtotal = q.Lines.Sum(l => Math.Round(l.Quantity * l.UnitPrice, 2)),
                q.TaxRate,
                q.ExpirationDate,
                q.CreatedAt,
            })
            .ToListAsync(ct);

        return rows.Select(r => new QuoteListItemModel(
            r.Id,
            r.QuoteNumber,
            r.CustomerId,
            r.CustomerName,
            r.Status.ToString(),
            r.LineCount,
            r.Subtotal + Money.Round(r.Subtotal * r.TaxRate),
            r.ExpirationDate,
            r.CreatedAt)).ToList();
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

    public Task<string> GenerateNextQuoteNumberAsync(CancellationToken ct)
        => DocumentNumberSequence.NextAsync(
            db.Quotes
                .IgnoreQueryFilters()
                .Where(q => q.Type == QuoteType.Quote && q.QuoteNumber != null)
                .Select(q => q.QuoteNumber!),
            "QT",
            ct);

    public Task<bool> QuoteNumberExistsAsync(string number, int? excludeId, CancellationToken ct)
    {
        var query = db.Quotes.Where(q => q.QuoteNumber == number);
        if (excludeId.HasValue)
            query = query.Where(q => q.Id != excludeId.Value);
        return query.AnyAsync(ct);
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
