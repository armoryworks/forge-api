using Forge.Core.Entities;
using Forge.Core.Enums;
using Forge.Core.Models;

namespace Forge.Core.Interfaces;

public interface ILeadRepository
{
    Task<List<LeadResponseModel>> GetLeadsAsync(LeadStatus? status, string? search, CancellationToken ct);
    Task<LeadResponseModel?> GetByIdAsync(int id, CancellationToken ct);
    Task<Lead?> FindAsync(int id, CancellationToken ct);
    Task AddAsync(Lead lead, CancellationToken ct);
    Task SaveChangesAsync(CancellationToken ct);
}
