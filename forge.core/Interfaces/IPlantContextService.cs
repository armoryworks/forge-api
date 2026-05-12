using Forge.Core.Entities;

namespace Forge.Core.Interfaces;

public interface IPlantContextService
{
    int? CurrentPlantId { get; }
    void SetPlant(int plantId);
    Task<IReadOnlyList<Plant>> GetUserPlantsAsync(int userId, CancellationToken ct);
}
