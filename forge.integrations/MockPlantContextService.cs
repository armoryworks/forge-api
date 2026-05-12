using Microsoft.Extensions.Logging;

using Forge.Core.Entities;
using Forge.Core.Interfaces;

namespace Forge.Integrations;

public class MockPlantContextService(ILogger<MockPlantContextService> logger) : IPlantContextService
{
    private int? _currentPlantId;

    public int? CurrentPlantId => _currentPlantId;

    public void SetPlant(int plantId)
    {
        logger.LogInformation("[MockPlantContext] SetPlant to {PlantId}", plantId);
        _currentPlantId = plantId;
    }

    public Task<IReadOnlyList<Plant>> GetUserPlantsAsync(int userId, CancellationToken ct)
    {
        logger.LogInformation("[MockPlantContext] GetUserPlants for user {UserId}", userId);
        IReadOnlyList<Plant> plants = [];
        return Task.FromResult(plants);
    }
}
