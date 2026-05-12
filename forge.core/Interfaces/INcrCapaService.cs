using Forge.Core.Entities;
using Forge.Core.Models;

namespace Forge.Core.Interfaces;

public interface INcrCapaService
{
    Task<string> GenerateNcrNumberAsync(CancellationToken ct);
    Task<string> GenerateCapaNumberAsync(CancellationToken ct);
    Task<CorrectiveAction> CreateCapaFromNcrAsync(int ncrId, int ownerId, CancellationToken ct);
    Task<CorrectiveAction> AdvanceCapaPhaseAsync(int capaId, CancellationToken ct);
    Task<bool> CanAdvanceCapaAsync(int capaId, CancellationToken ct);
    Task ScheduleEffectivenessCheckAsync(int capaId, DateTimeOffset checkDate, CancellationToken ct);
    Task<NcrCostSummary> CalculateNcrCostsAsync(int ncrId, CancellationToken ct);
}
