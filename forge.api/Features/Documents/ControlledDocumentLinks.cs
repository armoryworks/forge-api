using Microsoft.EntityFrameworkCore;

using Forge.Core.Entities;
using Forge.Data.Context;

namespace Forge.Api.Features.Documents;

/// <summary>The part and routing-operation a controlled document (typically a work instruction) applies to.</summary>
public static class ControlledDocumentLinks
{
    /// <summary>
    /// Validates the links and fills the part from the operation when only the operation is given.
    /// An operation that belongs to a different part than the one named is refused.
    /// </summary>
    public static async Task<(int? PartId, int? OperationId)> ResolveAsync(
        AppDbContext db, int? partId, int? operationId, CancellationToken ct)
    {
        if (operationId is int opId)
        {
            var operationPartId = await db.Operations.AsNoTracking()
                .Where(o => o.Id == opId)
                .Select(o => (int?)o.PartId)
                .FirstOrDefaultAsync(ct)
                ?? throw new KeyNotFoundException($"Operation {opId} not found.");
            if (partId is int named && named != operationPartId)
                throw new InvalidOperationException("The operation belongs to a different part.");
            return (operationPartId, opId);
        }

        if (partId is int pId && !await db.Parts.AsNoTracking().AnyAsync(p => p.Id == pId, ct))
            throw new KeyNotFoundException($"Part {pId} not found.");
        return (partId, null);
    }

    /// <summary>Display labels per document id: the part number and "op {step} — {title}" for the operation.</summary>
    public static async Task<Dictionary<int, (string? PartNumber, string? OperationLabel)>> LabelsAsync(
        AppDbContext db, IReadOnlyCollection<ControlledDocument> documents, CancellationToken ct)
    {
        var partIds = documents.Where(d => d.PartId != null).Select(d => d.PartId!.Value).Distinct().ToList();
        var operationIds = documents.Where(d => d.OperationId != null).Select(d => d.OperationId!.Value).Distinct().ToList();
        if (partIds.Count == 0 && operationIds.Count == 0)
            return [];

        var partNumbers = await db.Parts.AsNoTracking()
            .Where(p => partIds.Contains(p.Id))
            .ToDictionaryAsync(p => p.Id, p => p.PartNumber, ct);
        var operations = await db.Operations.AsNoTracking()
            .Where(o => operationIds.Contains(o.Id))
            .ToDictionaryAsync(o => o.Id, o => $"op {o.StepNumber} — {o.Title}", ct);

        return documents.ToDictionary(
            d => d.Id,
            d => (
                d.PartId is int p ? partNumbers.GetValueOrDefault(p) : null,
                d.OperationId is int o ? operations.GetValueOrDefault(o) : null));
    }
}
