using System.Text.Json;

using MediatR;

using Microsoft.EntityFrameworkCore;

using Forge.Core.Models;
using Forge.Data.Context;

namespace Forge.Api.Features.ComplianceForms;

/// <summary>
/// Retrieve the stored visual comparison result for a specific FormDefinitionVersion.
/// </summary>
public record GetComparisonResultQuery(int VersionId) : IRequest<VisualComparisonResult?>;

public class GetComparisonResultHandler(AppDbContext db)
    : IRequestHandler<GetComparisonResultQuery, VisualComparisonResult?>
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
    };

    public async Task<VisualComparisonResult?> Handle(
        GetComparisonResultQuery request, CancellationToken ct)
    {
        var version = await db.FormDefinitionVersions
            .FirstOrDefaultAsync(v => v.Id == request.VersionId, ct)
            ?? throw new KeyNotFoundException($"FormDefinitionVersion {request.VersionId} not found");

        if (string.IsNullOrEmpty(version.VisualComparisonJson))
            return null;

        return JsonSerializer.Deserialize<VisualComparisonResult>(version.VisualComparisonJson, JsonOptions);
    }
}
