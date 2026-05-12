using MediatR;
using Microsoft.EntityFrameworkCore;

using Forge.Core.Entities;
using Forge.Core.Models;
using Forge.Data.Context;
using Forge.Data.Extensions;

namespace Forge.Api.Features.IcpRubrics;

/// <summary>
/// Phase 1r / Batch 10 — ICP rubric CRUD. Dimensions are owned children;
/// the GetById handler eager-loads them, and the SaveDimensions command
/// upserts/deletes children in one pass (admins typically edit several
/// at a time in the rubric editor).
///
/// IsDefault enforces a single-default invariant — setting one rubric
/// default clears the flag on every other row.
/// </summary>
public record GetIcpRubricsQuery(bool? ActiveOnly) : IRequest<List<IcpRubricResponseModel>>;

public class GetIcpRubricsHandler(AppDbContext db)
    : IRequestHandler<GetIcpRubricsQuery, List<IcpRubricResponseModel>>
{
    public async Task<List<IcpRubricResponseModel>> Handle(GetIcpRubricsQuery request, CancellationToken ct)
    {
        var query = db.IcpRubrics.AsNoTracking();
        if (request.ActiveOnly == true) query = query.Where(r => r.IsActive);

        var rubrics = await query.OrderByDescending(r => r.IsDefault).ThenBy(r => r.Name).ToListAsync(ct);
        var ids = rubrics.Select(r => r.Id).ToList();
        var counts = await db.IcpDimensions.AsNoTracking()
            .Where(d => ids.Contains(d.IcpRubricId))
            .GroupBy(d => d.IcpRubricId)
            .Select(g => new { RubricId = g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.RubricId, x => x.Count, ct);

        return rubrics.Select(r => new IcpRubricResponseModel(
            r.Id, r.Name, r.Description, r.IsActive, r.IsDefault,
            counts.GetValueOrDefault(r.Id, 0), r.CreatedAt)).ToList();
    }
}

public record GetIcpRubricByIdQuery(int Id) : IRequest<IcpRubricDetailResponseModel>;

public class GetIcpRubricByIdHandler(AppDbContext db)
    : IRequestHandler<GetIcpRubricByIdQuery, IcpRubricDetailResponseModel>
{
    public async Task<IcpRubricDetailResponseModel> Handle(GetIcpRubricByIdQuery request, CancellationToken ct)
    {
        var rubric = await db.IcpRubrics.AsNoTracking()
            .Include(r => r.Dimensions)
            .FirstOrDefaultAsync(r => r.Id == request.Id, ct)
            ?? throw new KeyNotFoundException($"ICP rubric {request.Id} not found.");

        var dims = rubric.Dimensions
            .OrderBy(d => d.FieldKey)
            .Select(d => new IcpDimensionResponseModel(d.Id, d.IcpRubricId, d.FieldKey, d.Label, d.MatchSpec, d.Weight))
            .ToList();

        return new IcpRubricDetailResponseModel(
            rubric.Id, rubric.Name, rubric.Description, rubric.IsActive, rubric.IsDefault, dims, rubric.CreatedAt);
    }
}

public record CreateIcpRubricCommand(CreateIcpRubricRequest Request) : IRequest<IcpRubricResponseModel>;

public class CreateIcpRubricHandler(AppDbContext db) : IRequestHandler<CreateIcpRubricCommand, IcpRubricResponseModel>
{
    public async Task<IcpRubricResponseModel> Handle(CreateIcpRubricCommand request, CancellationToken ct)
    {
        var r = request.Request;
        if (string.IsNullOrWhiteSpace(r.Name)) throw new InvalidOperationException("Rubric name is required.");

        var rubric = new IcpRubric
        {
            Name = r.Name.Trim(),
            Description = r.Description?.Trim(),
            IsActive = true,
            IsDefault = false,
        };
        db.IcpRubrics.Add(rubric);
        db.LogActivityAt("icp-rubric-created", $"Created ICP rubric '{rubric.Name}'.", ("IcpRubric", 0));
        await db.SaveChangesAsync(ct);
        return new IcpRubricResponseModel(
            rubric.Id, rubric.Name, rubric.Description, rubric.IsActive, rubric.IsDefault, 0, rubric.CreatedAt);
    }
}

public record UpdateIcpRubricCommand(int Id, UpdateIcpRubricRequest Request) : IRequest<IcpRubricResponseModel>;

public class UpdateIcpRubricHandler(AppDbContext db) : IRequestHandler<UpdateIcpRubricCommand, IcpRubricResponseModel>
{
    public async Task<IcpRubricResponseModel> Handle(UpdateIcpRubricCommand request, CancellationToken ct)
    {
        var rubric = await db.IcpRubrics.FirstOrDefaultAsync(x => x.Id == request.Id, ct)
            ?? throw new KeyNotFoundException($"ICP rubric {request.Id} not found.");

        var r = request.Request;
        var changed = new List<string>();
        if (!string.IsNullOrWhiteSpace(r.Name) && r.Name.Trim() != rubric.Name)
        {
            rubric.Name = r.Name.Trim();
            changed.Add("name");
        }
        if (r.Description?.Trim() != rubric.Description)
        {
            rubric.Description = r.Description?.Trim();
            changed.Add("description");
        }
        if (r.IsActive != rubric.IsActive)
        {
            rubric.IsActive = r.IsActive;
            changed.Add($"isActive: {rubric.IsActive}");
        }
        if (r.IsDefault != rubric.IsDefault)
        {
            // Single-default invariant: clear the flag on every other rubric
            // before setting it here. Skipping the .Where(... != rubric.Id)
            // would clobber the row we're about to flip.
            if (r.IsDefault)
            {
                var others = await db.IcpRubrics.Where(x => x.Id != rubric.Id && x.IsDefault).ToListAsync(ct);
                foreach (var o in others) o.IsDefault = false;
            }
            rubric.IsDefault = r.IsDefault;
            changed.Add($"isDefault: {rubric.IsDefault}");
        }

        if (changed.Count > 0)
        {
            db.LogActivityAt("icp-rubric-updated",
                $"Updated {changed.Count} field(s): {string.Join(", ", changed)}",
                ("IcpRubric", rubric.Id));
        }

        await db.SaveChangesAsync(ct);
        var count = await db.IcpDimensions.AsNoTracking().CountAsync(d => d.IcpRubricId == rubric.Id, ct);
        return new IcpRubricResponseModel(
            rubric.Id, rubric.Name, rubric.Description, rubric.IsActive, rubric.IsDefault, count, rubric.CreatedAt);
    }
}

public record SaveIcpDimensionsCommand(int RubricId, List<SaveIcpDimensionRequest> Dimensions) : IRequest<IcpRubricDetailResponseModel>;

public class SaveIcpDimensionsHandler(AppDbContext db) : IRequestHandler<SaveIcpDimensionsCommand, IcpRubricDetailResponseModel>
{
    public async Task<IcpRubricDetailResponseModel> Handle(SaveIcpDimensionsCommand request, CancellationToken ct)
    {
        var rubric = await db.IcpRubrics
            .Include(r => r.Dimensions)
            .FirstOrDefaultAsync(r => r.Id == request.RubricId, ct)
            ?? throw new KeyNotFoundException($"ICP rubric {request.RubricId} not found.");

        // Bulk upsert: any existing dimension whose id isn't in the request
        // is deleted; matched ids are updated in place; new requests with
        // null id are inserted.
        var keepIds = request.Dimensions.Where(d => d.Id.HasValue).Select(d => d.Id!.Value).ToHashSet();
        var toRemove = rubric.Dimensions.Where(d => !keepIds.Contains(d.Id)).ToList();
        foreach (var r in toRemove) db.IcpDimensions.Remove(r);

        foreach (var req in request.Dimensions)
        {
            if (req.Id.HasValue)
            {
                var existing = rubric.Dimensions.FirstOrDefault(d => d.Id == req.Id.Value);
                if (existing == null) continue;
                existing.FieldKey = req.FieldKey.Trim();
                existing.Label = req.Label?.Trim();
                existing.MatchSpec = req.MatchSpec?.Trim();
                existing.Weight = req.Weight;
            }
            else
            {
                db.IcpDimensions.Add(new IcpDimension
                {
                    IcpRubricId = rubric.Id,
                    FieldKey = req.FieldKey.Trim(),
                    Label = req.Label?.Trim(),
                    MatchSpec = req.MatchSpec?.Trim(),
                    Weight = req.Weight,
                });
            }
        }

        db.LogActivityAt("icp-rubric-dimensions-updated",
            $"Updated rubric dimensions ({request.Dimensions.Count} after, {toRemove.Count} removed).",
            ("IcpRubric", rubric.Id));

        await db.SaveChangesAsync(ct);

        var refreshed = await db.IcpRubrics.AsNoTracking()
            .Include(r => r.Dimensions)
            .FirstAsync(r => r.Id == rubric.Id, ct);

        var dims = refreshed.Dimensions
            .OrderBy(d => d.FieldKey)
            .Select(d => new IcpDimensionResponseModel(d.Id, d.IcpRubricId, d.FieldKey, d.Label, d.MatchSpec, d.Weight))
            .ToList();

        return new IcpRubricDetailResponseModel(
            refreshed.Id, refreshed.Name, refreshed.Description, refreshed.IsActive, refreshed.IsDefault, dims, refreshed.CreatedAt);
    }
}

public record DeleteIcpRubricCommand(int Id) : IRequest;

public class DeleteIcpRubricHandler(AppDbContext db) : IRequestHandler<DeleteIcpRubricCommand>
{
    public async Task Handle(DeleteIcpRubricCommand request, CancellationToken ct)
    {
        var rubric = await db.IcpRubrics.FirstOrDefaultAsync(x => x.Id == request.Id, ct)
            ?? throw new KeyNotFoundException($"ICP rubric {request.Id} not found.");

        if (rubric.IsDefault)
            throw new InvalidOperationException(
                "Cannot delete the default rubric. Mark another rubric default first, then retry.");

        rubric.DeletedAt = DateTimeOffset.UtcNow;
        db.LogActivityAt("icp-rubric-deleted", $"Deleted ICP rubric '{rubric.Name}'.", ("IcpRubric", rubric.Id));
        await db.SaveChangesAsync(ct);
    }
}
