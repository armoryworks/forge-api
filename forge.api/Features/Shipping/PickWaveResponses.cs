using Forge.Core.Entities;
using Forge.Core.Models;

namespace Forge.Api.Features.Shipping;

public static class PickWaveResponses
{
    public static PickWaveResponseModel Summary(PickWave wave, string? assignedToName = null) => new()
    {
        Id = wave.Id,
        WaveNumber = wave.WaveNumber,
        Status = wave.Status,
        Strategy = wave.Strategy,
        AssignedToId = wave.AssignedToId,
        AssignedToName = assignedToName,
        TotalLines = wave.TotalLines,
        PickedLines = wave.PickedLines,
        ReleasedAt = wave.ReleasedAt,
        StartedAt = wave.StartedAt,
        CompletedAt = wave.CompletedAt,
        Notes = wave.Notes,
        CreatedAt = wave.CreatedAt,
    };
}
