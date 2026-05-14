using Microsoft.EntityFrameworkCore;

using Forge.Core.Entities;
using Forge.Core.Interfaces;
using Forge.Core.Models;
using Forge.Data.Context;

namespace Forge.Api.Services;

public class NcrCapaService(AppDbContext db, IClock clock) : INcrCapaService
{
    public async Task<string> GenerateNcrNumberAsync(CancellationToken ct)
    {
        var today = clock.UtcNow;
        var datePrefix = today.ToString("yyyyMMdd");
        var pattern = $"NCR-{datePrefix}-%";

        var lastNumber = await db.NonConformances
            .IgnoreQueryFilters()
            .Where(n => EF.Functions.Like(n.NcrNumber, pattern))
            .OrderByDescending(n => n.NcrNumber)
            .Select(n => n.NcrNumber)
            .FirstOrDefaultAsync(ct);

        var sequence = 1;
        if (lastNumber != null)
        {
            var parts = lastNumber.Split('-');
            if (parts.Length == 3 && int.TryParse(parts[2], out var lastSeq))
                sequence = lastSeq + 1;
        }

        return $"NCR-{datePrefix}-{sequence:D3}";
    }

    public async Task<string> GenerateCapaNumberAsync(CancellationToken ct)
    {
        var today = clock.UtcNow;
        var datePrefix = today.ToString("yyyyMMdd");
        var pattern = $"CAPA-{datePrefix}-%";

        var lastNumber = await db.CorrectiveActions
            .IgnoreQueryFilters()
            .Where(c => EF.Functions.Like(c.CapaNumber, pattern))
            .OrderByDescending(c => c.CapaNumber)
            .Select(c => c.CapaNumber)
            .FirstOrDefaultAsync(ct);

        var sequence = 1;
        if (lastNumber != null)
        {
            var parts = lastNumber.Split('-');
            if (parts.Length == 3 && int.TryParse(parts[2], out var lastSeq))
                sequence = lastSeq + 1;
        }

        return $"CAPA-{datePrefix}-{sequence:D3}";
    }

    public async Task<CorrectiveAction> CreateCapaFromNcrAsync(int ncrId, int ownerId, CancellationToken ct)
    {
        var ncr = await db.NonConformances
            .Include(n => n.Part)
            .FirstOrDefaultAsync(n => n.Id == ncrId, ct)
            ?? throw new KeyNotFoundException($"NCR {ncrId} not found");

        var capaNumber = await GenerateCapaNumberAsync(ct);

        var capa = new CorrectiveAction
        {
            CapaNumber = capaNumber,
            Type = CapaType.Corrective,
            SourceType = CapaSourceType.Ncr,
            SourceEntityId = ncrId,
            SourceEntityType = "NonConformance",
            Title = $"CAPA for {ncr.NcrNumber} — {ncr.Part.PartNumber}",
            ProblemDescription = ncr.Description,
            OwnerId = ownerId,
            Status = CapaStatus.Open,
            Priority = 2,
            DueDate = clock.UtcNow.AddDays(30),
        };

        db.CorrectiveActions.Add(capa);
        await db.SaveChangesAsync(ct);

        ncr.CapaId = capa.Id;
        ncr.Status = NcrStatus.UnderReview;
        await db.SaveChangesAsync(ct);

        return capa;
    }

    public async Task<CorrectiveAction> AdvanceCapaPhaseAsync(int capaId, CancellationToken ct)
    {
        var capa = await db.CorrectiveActions
            .Include(c => c.Tasks)
            .FirstOrDefaultAsync(c => c.Id == capaId, ct)
            ?? throw new KeyNotFoundException($"CAPA {capaId} not found");

        if (!CanAdvancePhase(capa))
            throw new InvalidOperationException($"CAPA {capa.CapaNumber} cannot advance from {capa.Status}. Required fields are missing.");

        capa.Status = capa.Status switch
        {
            CapaStatus.Open => CapaStatus.RootCauseAnalysis,
            CapaStatus.RootCauseAnalysis => CapaStatus.ActionPlanning,
            CapaStatus.ActionPlanning => CapaStatus.Implementation,
            CapaStatus.Implementation => CapaStatus.Verification,
            CapaStatus.Verification => CapaStatus.EffectivenessCheck,
            CapaStatus.EffectivenessCheck => CapaStatus.Closed,
            _ => throw new InvalidOperationException($"Cannot advance from {capa.Status}")
        };

        if (capa.Status == CapaStatus.Closed)
            capa.ClosedAt = clock.UtcNow;

        await db.SaveChangesAsync(ct);
        return capa;
    }

    public async Task<bool> CanAdvanceCapaAsync(int capaId, CancellationToken ct)
    {
        var capa = await db.CorrectiveActions
            .Include(c => c.Tasks)
            .AsNoTracking()
            .FirstOrDefaultAsync(c => c.Id == capaId, ct)
            ?? throw new KeyNotFoundException($"CAPA {capaId} not found");

        return CanAdvancePhase(capa);
    }

    public async Task ScheduleEffectivenessCheckAsync(int capaId, DateTimeOffset checkDate, CancellationToken ct)
    {
        var capa = await db.CorrectiveActions
            .FirstOrDefaultAsync(c => c.Id == capaId, ct)
            ?? throw new KeyNotFoundException($"CAPA {capaId} not found");

        capa.EffectivenessCheckDueDate = checkDate;
        await db.SaveChangesAsync(ct);
    }

    public async Task<NcrCostSummary> CalculateNcrCostsAsync(int ncrId, CancellationToken ct)
    {
        var ncr = await db.NonConformances
            .AsNoTracking()
            .FirstOrDefaultAsync(n => n.Id == ncrId, ct)
            ?? throw new KeyNotFoundException($"NCR {ncrId} not found");

        var materialCost = ncr.MaterialCost ?? 0m;
        var laborCost = ncr.LaborCost ?? 0m;
        var totalCost = ncr.TotalCostImpact ?? (materialCost + laborCost);
        var affectedQty = (int)ncr.AffectedQuantity;
        var costPerUnit = affectedQty > 0 ? totalCost / affectedQty : 0m;

        return new NcrCostSummary
        {
            MaterialCost = materialCost,
            LaborCost = laborCost,
            TotalCost = totalCost,
            AffectedQuantity = affectedQty,
            CostPerUnit = costPerUnit,
        };
    }

    private static bool CanAdvancePhase(CorrectiveAction capa)
    {
        return capa.Status switch
        {
            CapaStatus.Open => true,
            CapaStatus.RootCauseAnalysis => !string.IsNullOrWhiteSpace(capa.RootCauseAnalysis) && capa.RootCauseMethod.HasValue,
            CapaStatus.ActionPlanning => !string.IsNullOrWhiteSpace(capa.CorrectiveActionDescription),
            CapaStatus.Implementation => capa.Tasks.Count == 0 || capa.Tasks.All(t => t.Status is CapaTaskStatus.Completed or CapaTaskStatus.Cancelled),
            CapaStatus.Verification => !string.IsNullOrWhiteSpace(capa.VerificationResult),
            CapaStatus.EffectivenessCheck => capa.IsEffective.HasValue,
            CapaStatus.Closed => false,
            _ => false
        };
    }
}
