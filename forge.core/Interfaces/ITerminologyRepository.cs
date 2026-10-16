using Forge.Core.Models;

namespace Forge.Core.Interfaces;

public interface ITerminologyRepository
{
    Task<List<TerminologyEntryResponseModel>> GetAllAsync(CancellationToken ct);
}
