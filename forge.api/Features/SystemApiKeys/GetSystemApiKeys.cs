using System.Text.Json;

using MediatR;
using Microsoft.EntityFrameworkCore;

using Forge.Core.Models;
using Forge.Data.Context;

namespace Forge.Api.Features.SystemApiKeys;

public record GetSystemApiKeysQuery : IRequest<List<SystemApiKeyResponseModel>>;

public class GetSystemApiKeysHandler(AppDbContext db)
    : IRequestHandler<GetSystemApiKeysQuery, List<SystemApiKeyResponseModel>>
{
    public async Task<List<SystemApiKeyResponseModel>> Handle(
        GetSystemApiKeysQuery request, CancellationToken cancellationToken)
    {
        // Join to ApplicationUser + RoleTemplate so the admin UI can show
        // "key X belongs to user Y, scoped to template Z" without round-trips.
        var rows = await (from k in db.SystemApiKeys.AsNoTracking()
                          join u in db.Users.AsNoTracking() on k.UserId equals u.Id into uj
                          from u in uj.DefaultIfEmpty()
                          join t in db.RoleTemplates.AsNoTracking()
                              on k.RoleTemplateId equals t.Id into tj
                          from t in tj.DefaultIfEmpty()
                          orderby k.CreatedAt descending
                          select new
                          {
                              Key = k,
                              UserEmail = u != null ? u.Email : null,
                              RoleTemplateName = t != null ? t.Name : null,
                          })
            .ToListAsync(cancellationToken);

        return rows.Select(r => new SystemApiKeyResponseModel
        {
            Id = r.Key.Id,
            Name = r.Key.Name,
            KeyPrefix = r.Key.KeyPrefix,
            UserId = r.Key.UserId,
            UserEmail = r.UserEmail,
            IsActive = r.Key.IsActive,
            LastUsedAt = r.Key.LastUsedAt,
            ExpiresAt = r.Key.ExpiresAt,
            Scopes = r.Key.ScopesJson != null
                ? JsonSerializer.Deserialize<List<string>>(r.Key.ScopesJson) : null,
            AllowedIps = r.Key.AllowedIpsJson != null
                ? JsonSerializer.Deserialize<List<string>>(r.Key.AllowedIpsJson) : null,
            CreatedAt = r.Key.CreatedAt,
            RoleTemplateId = r.Key.RoleTemplateId,
            RoleTemplateName = r.RoleTemplateName,
        }).ToList();
    }
}
