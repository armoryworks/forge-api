using MediatR;
using Microsoft.EntityFrameworkCore;

using Forge.Core.Entities;
using Forge.Core.Models;
using Forge.Data.Context;
using Forge.Data.Extensions;

namespace Forge.Api.Features.LeadSources;

/// <summary>
/// Phase 1r / Batch 9 — LeadSource CRUD handlers. Code is required +
/// immutable after creation (referenced by import pipelines and forms);
/// Name and Description are admin-editable. QualityScore + LastScoredAt
/// are owned by the nightly recompute job, not the admin UI.
/// </summary>
public record GetLeadSourcesQuery(bool? ActiveOnly) : IRequest<List<LeadSourceResponseModel>>;

public class GetLeadSourcesHandler(AppDbContext db)
    : IRequestHandler<GetLeadSourcesQuery, List<LeadSourceResponseModel>>
{
    public async Task<List<LeadSourceResponseModel>> Handle(GetLeadSourcesQuery request, CancellationToken ct)
    {
        var query = db.LeadSources.AsNoTracking();
        if (request.ActiveOnly == true) query = query.Where(s => s.IsActive);

        var sources = await query.OrderBy(s => s.Name).ToListAsync(ct);
        var ids = sources.Select(s => s.Id).ToList();

        var counts = await db.Leads.AsNoTracking()
            .Where(l => l.LeadSourceId != null && ids.Contains(l.LeadSourceId.Value))
            .GroupBy(l => l.LeadSourceId!.Value)
            .Select(g => new { SourceId = g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.SourceId, x => x.Count, ct);

        return sources.Select(s => new LeadSourceResponseModel(
            s.Id, s.Name, s.Code, s.Description, s.QualityScore, s.LastScoredAt,
            s.IsActive, counts.GetValueOrDefault(s.Id, 0), s.CreatedAt)).ToList();
    }
}

public record CreateLeadSourceCommand(CreateLeadSourceRequest Request) : IRequest<LeadSourceResponseModel>;

public class CreateLeadSourceHandler(AppDbContext db) : IRequestHandler<CreateLeadSourceCommand, LeadSourceResponseModel>
{
    public async Task<LeadSourceResponseModel> Handle(CreateLeadSourceCommand request, CancellationToken ct)
    {
        var r = request.Request;
        if (string.IsNullOrWhiteSpace(r.Name))
            throw new InvalidOperationException("Source name is required.");
        if (string.IsNullOrWhiteSpace(r.Code))
            throw new InvalidOperationException("Source code is required.");

        var code = r.Code.Trim();
        if (await db.LeadSources.AnyAsync(s => s.Code == code, ct))
            throw new InvalidOperationException($"A lead source with code '{code}' already exists.");

        var s = new LeadSource
        {
            Name = r.Name.Trim(),
            Code = code,
            Description = r.Description?.Trim(),
            IsActive = true,
            QualityScore = 50,
        };
        db.LeadSources.Add(s);

        db.LogActivityAt("lead-source-created",
            $"Created lead source '{s.Name}' ({s.Code}).",
            ("LeadSource", 0));

        await db.SaveChangesAsync(ct);

        return new LeadSourceResponseModel(
            s.Id, s.Name, s.Code, s.Description, s.QualityScore, s.LastScoredAt,
            s.IsActive, 0, s.CreatedAt);
    }
}

public record UpdateLeadSourceCommand(int Id, UpdateLeadSourceRequest Request) : IRequest<LeadSourceResponseModel>;

public class UpdateLeadSourceHandler(AppDbContext db) : IRequestHandler<UpdateLeadSourceCommand, LeadSourceResponseModel>
{
    public async Task<LeadSourceResponseModel> Handle(UpdateLeadSourceCommand request, CancellationToken ct)
    {
        var s = await db.LeadSources.FirstOrDefaultAsync(x => x.Id == request.Id, ct)
            ?? throw new KeyNotFoundException($"Lead source {request.Id} not found.");

        var r = request.Request;
        var changed = new List<string>();
        if (!string.IsNullOrWhiteSpace(r.Name) && r.Name.Trim() != s.Name)
        {
            s.Name = r.Name.Trim();
            changed.Add("name");
        }
        if (r.Description?.Trim() != s.Description)
        {
            s.Description = r.Description?.Trim();
            changed.Add("description");
        }
        if (r.IsActive != s.IsActive)
        {
            s.IsActive = r.IsActive;
            changed.Add($"isActive: {s.IsActive}");
        }

        if (changed.Count > 0)
        {
            db.LogActivityAt("lead-source-updated",
                $"Updated {changed.Count} field(s): {string.Join(", ", changed)}",
                ("LeadSource", s.Id));
        }

        await db.SaveChangesAsync(ct);

        var leadCount = await db.Leads.AsNoTracking().CountAsync(l => l.LeadSourceId == s.Id, ct);
        return new LeadSourceResponseModel(
            s.Id, s.Name, s.Code, s.Description, s.QualityScore, s.LastScoredAt,
            s.IsActive, leadCount, s.CreatedAt);
    }
}

public record DeleteLeadSourceCommand(int Id) : IRequest;

public class DeleteLeadSourceHandler(AppDbContext db) : IRequestHandler<DeleteLeadSourceCommand>
{
    public async Task Handle(DeleteLeadSourceCommand request, CancellationToken ct)
    {
        var s = await db.LeadSources.FirstOrDefaultAsync(x => x.Id == request.Id, ct)
            ?? throw new KeyNotFoundException($"Lead source {request.Id} not found.");

        // Refuse to delete if any lead points at this source. The admin can
        // deactivate (IsActive=false) to hide it from new intake forms while
        // keeping historical attribution intact.
        var leadCount = await db.Leads.AsNoTracking().CountAsync(l => l.LeadSourceId == s.Id, ct);
        if (leadCount > 0)
            throw new InvalidOperationException(
                $"Cannot delete source — {leadCount} lead(s) reference it. Deactivate instead.");

        s.DeletedAt = DateTimeOffset.UtcNow;

        db.LogActivityAt("lead-source-deleted",
            $"Deleted lead source '{s.Name}' ({s.Code}).",
            ("LeadSource", s.Id));

        await db.SaveChangesAsync(ct);
    }
}
