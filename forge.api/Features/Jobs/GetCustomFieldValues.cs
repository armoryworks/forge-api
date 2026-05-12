using System.Text.Json;

using MediatR;

using Forge.Core.Interfaces;

namespace Forge.Api.Features.Jobs;

public record GetCustomFieldValuesQuery(int JobId) : IRequest<Dictionary<string, object?>>;

public class GetCustomFieldValuesHandler(
    IJobRepository repo) : IRequestHandler<GetCustomFieldValuesQuery, Dictionary<string, object?>>
{
    public async Task<Dictionary<string, object?>> Handle(GetCustomFieldValuesQuery request, CancellationToken ct)
    {
        var job = await repo.FindAsync(request.JobId, ct)
            ?? throw new KeyNotFoundException($"Job with ID {request.JobId} not found.");

        if (string.IsNullOrWhiteSpace(job.CustomFieldValues))
            return new Dictionary<string, object?>();

        return JsonSerializer.Deserialize<Dictionary<string, object?>>(
            job.CustomFieldValues,
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true }) ?? new Dictionary<string, object?>();
    }
}
