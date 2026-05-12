using Forge.Core.Entities;
using Forge.Core.Models;

namespace Forge.Core.Interfaces;

public interface ITerminologyRepository
{
    Task<List<TerminologyEntryResponseModel>> GetAllAsync(CancellationToken ct);
    Task<TerminologyEntry?> FindByKeyAsync(string key, CancellationToken ct);
    Task AddAsync(TerminologyEntry entry, CancellationToken ct);
    Task SaveChangesAsync(CancellationToken ct);
}
