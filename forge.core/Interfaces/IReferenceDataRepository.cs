using Forge.Core.Models;

namespace Forge.Core.Interfaces;

public interface IReferenceDataRepository
{
    Task<List<ReferenceDataGroupResponseModel>> GetAllGroupsAsync(CancellationToken ct);
    Task<List<ReferenceDataResponseModel>> GetByGroupAsync(string groupCode, CancellationToken ct);
}
