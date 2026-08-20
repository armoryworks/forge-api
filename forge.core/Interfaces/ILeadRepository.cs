using Forge.Core.Entities;
using Forge.Core.Enums;
using Forge.Core.Models;

namespace Forge.Core.Interfaces;

public interface ILeadRepository
{
    Task<List<LeadResponseModel>> GetLeadsAsync(LeadStatus? status, string? search, string? externalId, CancellationToken ct);
    Task<LeadResponseModel?> GetByIdAsync(int id, CancellationToken ct);
    Task<Lead?> FindAsync(int id, CancellationToken ct);

    /// <summary>Next auto-generated lead number in the <c>LEAD-#####</c> series.</summary>
    Task<string> GenerateNextLeadNumberAsync(CancellationToken ct);

    /// <summary>True when <paramref name="number"/> is already in use, optionally excluding one lead id.</summary>
    Task<bool> LeadNumberExistsAsync(string number, int? excludeId, CancellationToken ct);

    Task AddAsync(Lead lead, CancellationToken ct);
    Task SaveChangesAsync(CancellationToken ct);
}
