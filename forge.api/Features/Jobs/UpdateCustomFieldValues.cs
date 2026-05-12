using System.Text.Json;

using FluentValidation;

using MediatR;

using Forge.Core.Interfaces;
using Forge.Core.Models;

namespace Forge.Api.Features.Jobs;

public record UpdateCustomFieldValuesCommand(
    int JobId,
    Dictionary<string, object?> Values) : IRequest<Dictionary<string, object?>>;

public class UpdateCustomFieldValuesValidator : AbstractValidator<UpdateCustomFieldValuesCommand>
{
    public UpdateCustomFieldValuesValidator()
    {
        RuleFor(x => x.JobId).GreaterThan(0);
        RuleFor(x => x.Values).NotNull();
    }
}

public class UpdateCustomFieldValuesHandler(
    IJobRepository repo,
    ITrackTypeRepository trackTypeRepo) : IRequestHandler<UpdateCustomFieldValuesCommand, Dictionary<string, object?>>
{
    public async Task<Dictionary<string, object?>> Handle(UpdateCustomFieldValuesCommand request, CancellationToken ct)
    {
        var job = await repo.FindAsync(request.JobId, ct)
            ?? throw new KeyNotFoundException($"Job with ID {request.JobId} not found.");

        var trackType = await trackTypeRepo.FindAsync(job.TrackTypeId, ct)
            ?? throw new KeyNotFoundException($"Track type with ID {job.TrackTypeId} not found.");

        // Validate against field definitions
        if (!string.IsNullOrWhiteSpace(trackType.CustomFieldDefinitions))
        {
            var definitions = JsonSerializer.Deserialize<List<CustomFieldDefinitionModel>>(
                trackType.CustomFieldDefinitions,
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true }) ?? [];

            ValidateAgainstDefinitions(request.Values, definitions);
        }

        var json = JsonSerializer.Serialize(request.Values, new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        });

        job.CustomFieldValues = json;
        await repo.SaveChangesAsync(ct);

        return request.Values;
    }

    private static void ValidateAgainstDefinitions(
        Dictionary<string, object?> values,
        List<CustomFieldDefinitionModel> definitions)
    {
        var definitionsByKey = definitions.ToDictionary(d => d.Key);

        // Check required fields are present and non-null
        foreach (var def in definitions.Where(d => d.IsRequired))
        {
            if (!values.TryGetValue(def.Key, out var value) || value is null)
                throw new InvalidOperationException($"Required custom field '{def.Label}' is missing.");
        }

        // Check submitted values match known fields and types
        foreach (var (key, value) in values)
        {
            if (!definitionsByKey.TryGetValue(key, out var def))
                continue; // Ignore unknown fields silently

            if (value is null)
                continue;

            ValidateFieldType(def, value);
        }
    }

    private static void ValidateFieldType(CustomFieldDefinitionModel def, object value)
    {
        switch (def.Type)
        {
            case "number":
                if (value is not JsonElement { ValueKind: JsonValueKind.Number })
                {
                    // Also accept numeric strings
                    if (value is JsonElement { ValueKind: JsonValueKind.String } strEl
                        && double.TryParse(strEl.GetString(), out _))
                        break;

                    throw new InvalidOperationException($"Custom field '{def.Label}' must be a number.");
                }
                break;

            case "toggle":
                if (value is not JsonElement { ValueKind: JsonValueKind.True or JsonValueKind.False })
                    throw new InvalidOperationException($"Custom field '{def.Label}' must be a boolean.");
                break;

            case "select":
                if (def.Options is not null && value is JsonElement selectEl)
                {
                    var strValue = selectEl.GetString();
                    if (strValue is not null && !def.Options.Contains(strValue))
                        throw new InvalidOperationException($"Custom field '{def.Label}' has an invalid option '{strValue}'.");
                }
                break;
        }
    }
}
