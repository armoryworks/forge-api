using Forge.Core.Entities;
using Forge.Core.Models;

namespace Forge.Core.Interfaces;

public interface IPickWaveService
{
    Task<PickWave> CreateWaveAsync(CreatePickWaveRequestModel request, CancellationToken ct);
    Task<PickWave> AutoGenerateWaveAsync(AutoWaveParametersModel parameters, CancellationToken ct);
    Task ReleaseWaveAsync(int waveId, CancellationToken ct);
    Task ConfirmPickLineAsync(int lineId, decimal pickedQuantity, string? shortNotes, CancellationToken ct);
    Task CompleteWaveAsync(int waveId, CancellationToken ct);
    Task<IReadOnlyList<PickLine>> OptimizePickPathAsync(int waveId, CancellationToken ct);
    Task PrintPickListAsync(int waveId, CancellationToken ct);
}
