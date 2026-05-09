using Microsoft.EntityFrameworkCore;
using QBEngineer.Core.Entities;
using QBEngineer.Core.Enums;
using QBEngineer.Core.Interfaces;
using QBEngineer.Core.Models;
using QBEngineer.Data.Context;

namespace QBEngineer.Data.Repositories;

public class LeadRepository(AppDbContext db) : ILeadRepository
{
    public async Task<List<LeadResponseModel>> GetLeadsAsync(LeadStatus? status, string? search, CancellationToken ct)
    {
        var query = db.Leads.AsQueryable();

        if (status.HasValue)
            query = query.Where(l => l.Status == status.Value);

        if (!string.IsNullOrWhiteSpace(search))
        {
            var term = search.Trim().ToLower();
            query = query.Where(l =>
                l.CompanyName.ToLower().Contains(term) ||
                (l.ContactName != null && l.ContactName.ToLower().Contains(term)) ||
                (l.Email != null && l.Email.ToLower().Contains(term)));
        }

        var leads = await query
            .OrderByDescending(l => l.CreatedAt)
            .ToListAsync(ct);

        return leads.Select(ToResponseModel).ToList();
    }

    public async Task<LeadResponseModel?> GetByIdAsync(int id, CancellationToken ct)
    {
        var lead = await db.Leads.FirstOrDefaultAsync(l => l.Id == id, ct);
        return lead is null ? null : ToResponseModel(lead);
    }

    public Task<Lead?> FindAsync(int id, CancellationToken ct)
        => db.Leads.FirstOrDefaultAsync(l => l.Id == id, ct);

    public async Task AddAsync(Lead lead, CancellationToken ct)
    {
        await db.Leads.AddAsync(lead, ct);
        await db.SaveChangesAsync(ct);
    }

    public Task SaveChangesAsync(CancellationToken ct)
        => db.SaveChangesAsync(ct);

    private static LeadResponseModel ToResponseModel(Lead l) => new(
        l.Id, l.CompanyName, l.ContactName, l.Email, l.Phone, l.Source,
        l.Status, l.Notes, l.FollowUpDate, l.LostReason,
        l.ConvertedCustomerId, l.CreatedAt, l.UpdatedAt,
        l.EngagementShape, l.CustomFieldValues);
}
