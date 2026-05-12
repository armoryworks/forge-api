using System.Security.Claims;

using MediatR;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;

using Forge.Core.Entities;
using Forge.Core.Models;
using Forge.Data.Context;
using Forge.Data.Extensions;

namespace Forge.Api.Features.Campaigns;

/// <summary>
/// Phase 1r / Batch 5 — campaign CRUD. Handlers grouped in one file
/// since they're each &lt;30 lines and share the same response shape.
/// Lead count is computed at read time (no denormalized counter on
/// the entity); for installs with thousands of campaigns we'd
/// promote it to a cached column.
/// </summary>
public record GetOutreachCampaignsQuery(bool? ActiveOnly) : IRequest<List<OutreachCampaignResponseModel>>;

public class GetOutreachCampaignsHandler(AppDbContext db)
    : IRequestHandler<GetOutreachCampaignsQuery, List<OutreachCampaignResponseModel>>
{
    public async Task<List<OutreachCampaignResponseModel>> Handle(GetOutreachCampaignsQuery request, CancellationToken ct)
    {
        var query = db.OutreachCampaigns.AsNoTracking();
        if (request.ActiveOnly == true) query = query.Where(c => c.IsActive);

        var campaigns = await query.OrderByDescending(c => c.CreatedAt).ToListAsync(ct);
        var ids = campaigns.Select(c => c.Id).ToList();

        var counts = await db.Leads.AsNoTracking()
            .Where(l => l.CampaignId != null && ids.Contains(l.CampaignId.Value))
            .GroupBy(l => l.CampaignId!.Value)
            .Select(g => new { CampaignId = g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.CampaignId, x => x.Count, ct);

        return campaigns.Select(c => new OutreachCampaignResponseModel(
            c.Id, c.Name, c.Description, c.Strategy, c.DefaultCooldownDays,
            c.StartedAt, c.EndedAt, c.IsActive, c.OwnerUserId,
            counts.GetValueOrDefault(c.Id, 0), c.CreatedAt)).ToList();
    }
}

public record CreateOutreachCampaignCommand(CreateOutreachCampaignRequest Request) : IRequest<OutreachCampaignResponseModel>;

public class CreateOutreachCampaignHandler(AppDbContext db, IHttpContextAccessor http)
    : IRequestHandler<CreateOutreachCampaignCommand, OutreachCampaignResponseModel>
{
    public async Task<OutreachCampaignResponseModel> Handle(CreateOutreachCampaignCommand request, CancellationToken ct)
    {
        var r = request.Request;
        if (string.IsNullOrWhiteSpace(r.Name))
            throw new InvalidOperationException("Campaign name is required.");

        var ownerId = int.TryParse(http.HttpContext?.User.FindFirstValue(ClaimTypes.NameIdentifier), out var uid) ? uid : (int?)null;

        var c = new OutreachCampaign
        {
            Name = r.Name.Trim(),
            Description = r.Description?.Trim(),
            Strategy = r.Strategy,
            DefaultCooldownDays = r.DefaultCooldownDays,
            StartedAt = r.StartedAt,
            EndedAt = r.EndedAt,
            IsActive = true,
            OwnerUserId = ownerId,
        };
        db.OutreachCampaigns.Add(c);
        await db.SaveChangesAsync(ct);

        return new OutreachCampaignResponseModel(
            c.Id, c.Name, c.Description, c.Strategy, c.DefaultCooldownDays,
            c.StartedAt, c.EndedAt, c.IsActive, c.OwnerUserId, 0, c.CreatedAt);
    }
}

public record UpdateOutreachCampaignCommand(int Id, UpdateOutreachCampaignRequest Request) : IRequest<OutreachCampaignResponseModel>;

public class UpdateOutreachCampaignHandler(AppDbContext db)
    : IRequestHandler<UpdateOutreachCampaignCommand, OutreachCampaignResponseModel>
{
    public async Task<OutreachCampaignResponseModel> Handle(UpdateOutreachCampaignCommand request, CancellationToken ct)
    {
        var c = await db.OutreachCampaigns.FirstOrDefaultAsync(x => x.Id == request.Id, ct)
            ?? throw new KeyNotFoundException($"Campaign {request.Id} not found.");

        var r = request.Request;
        var changed = new List<string>();
        if (r.Name?.Trim() != c.Name && !string.IsNullOrWhiteSpace(r.Name)) { c.Name = r.Name.Trim(); changed.Add("name"); }
        if (r.Description?.Trim() != c.Description) { c.Description = r.Description?.Trim(); changed.Add("description"); }
        if (r.DefaultCooldownDays != c.DefaultCooldownDays) { c.DefaultCooldownDays = r.DefaultCooldownDays; changed.Add("defaultCooldownDays"); }
        if (r.StartedAt != c.StartedAt) { c.StartedAt = r.StartedAt; changed.Add("startedAt"); }
        if (r.EndedAt != c.EndedAt) { c.EndedAt = r.EndedAt; changed.Add("endedAt"); }
        if (r.IsActive != c.IsActive) { c.IsActive = r.IsActive; changed.Add("isActive"); }

        if (changed.Count > 0)
        {
            db.LogActivityAt("campaign-updated",
                $"Updated {changed.Count} field(s): {string.Join(", ", changed)}",
                ("Campaign", c.Id));
        }

        await db.SaveChangesAsync(ct);

        var leadCount = await db.Leads.AsNoTracking().CountAsync(l => l.CampaignId == c.Id, ct);
        return new OutreachCampaignResponseModel(
            c.Id, c.Name, c.Description, c.Strategy, c.DefaultCooldownDays,
            c.StartedAt, c.EndedAt, c.IsActive, c.OwnerUserId, leadCount, c.CreatedAt);
    }
}
