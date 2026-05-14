using Microsoft.Extensions.Logging;

using Forge.Core.Entities;
using Forge.Core.Interfaces;
using Forge.Core.Models;

namespace Forge.Integrations;

public class MockPickWaveService(ILogger<MockPickWaveService> logger) : IPickWaveService
{
    public Task<PickWave> CreateWaveAsync(CreatePickWaveRequestModel request, CancellationToken ct)
    {
        logger.LogInformation("[MockPickWave] CreateWave with {LineCount} lines, Strategy={Strategy}", request.ShipmentLineIds.Count, request.Strategy);
        var wave = new PickWave
        {
            Id = 1,
            WaveNumber = "WAVE-0001",
            Status = PickWaveStatus.Draft,
            Strategy = request.Strategy,
            AssignedToId = request.AssignedToId,
            TotalLines = request.ShipmentLineIds.Count,
            PickedLines = 0,
            Notes = request.Notes,
        };
        return Task.FromResult(wave);
    }

    public Task<PickWave> AutoGenerateWaveAsync(AutoWaveParametersModel parameters, CancellationToken ct)
    {
        logger.LogInformation("[MockPickWave] AutoGenerateWave Strategy={Strategy}, MaxLines={MaxLines}", parameters.Strategy, parameters.MaxLinesPerWave);
        var wave = new PickWave
        {
            Id = 1,
            WaveNumber = "WAVE-0001",
            Status = PickWaveStatus.Draft,
            Strategy = parameters.Strategy,
            TotalLines = 0,
            PickedLines = 0,
        };
        return Task.FromResult(wave);
    }

    public Task ReleaseWaveAsync(int waveId, CancellationToken ct)
    {
        logger.LogInformation("[MockPickWave] ReleaseWave {WaveId}", waveId);
        return Task.CompletedTask;
    }

    public Task ConfirmPickLineAsync(int lineId, decimal pickedQuantity, string? shortNotes, CancellationToken ct)
    {
        logger.LogInformation("[MockPickWave] ConfirmPickLine {LineId}, Qty={Quantity}", lineId, pickedQuantity);
        return Task.CompletedTask;
    }

    public Task CompleteWaveAsync(int waveId, CancellationToken ct)
    {
        logger.LogInformation("[MockPickWave] CompleteWave {WaveId}", waveId);
        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<PickLine>> OptimizePickPathAsync(int waveId, CancellationToken ct)
    {
        logger.LogInformation("[MockPickWave] OptimizePickPath {WaveId}", waveId);
        return Task.FromResult<IReadOnlyList<PickLine>>([]);
    }

    public Task PrintPickListAsync(int waveId, CancellationToken ct)
    {
        logger.LogInformation("[MockPickWave] PrintPickList {WaveId}", waveId);
        return Task.CompletedTask;
    }
}
