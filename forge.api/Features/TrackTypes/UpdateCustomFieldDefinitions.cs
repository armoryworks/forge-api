using System.Text.Json;

using FluentValidation;

using MediatR;

using Forge.Core.Interfaces;
using Forge.Core.Models;

namespace Forge.Api.Features.TrackTypes;

public record UpdateCustomFieldDefinitionsCommand(
    int TrackTypeId,
    List<CustomFieldDefinitionModel> Fields) : IRequest<List<CustomFieldDefinitionModel>>;

public class UpdateCustomFieldDefinitionsValidator : AbstractValidator<UpdateCustomFieldDefinitionsCommand>
{
    private static readonly string[] ValidTypes = ["text", "number", "date", "select", "toggle"];

    public UpdateCustomFieldDefinitionsValidator()
    {
        RuleFor(x => x.TrackTypeId).GreaterThan(0);
        RuleForEach(x => x.Fields).ChildRules(field =>
        {
            field.RuleFor(f => f.Key).NotEmpty().MaximumLength(100);
            field.RuleFor(f => f.Label).NotEmpty().MaximumLength(200);
            field.RuleFor(f => f.Type)
                .NotEmpty()
                .Must(t => ValidTypes.Contains(t))
                .WithMessage("Type must be one of: text, number, date, select, toggle");
            field.RuleFor(f => f.Options)
                .NotEmpty()
                .When(f => f.Type == "select")
                .WithMessage("Options are required for select type fields");
        });
    }
}

public class UpdateCustomFieldDefinitionsHandler(
    ITrackTypeRepository repo) : IRequestHandler<UpdateCustomFieldDefinitionsCommand, List<CustomFieldDefinitionModel>>
{
    public async Task<List<CustomFieldDefinitionModel>> Handle(UpdateCustomFieldDefinitionsCommand request, CancellationToken ct)
    {
        var trackType = await repo.FindAsync(request.TrackTypeId, ct)
            ?? throw new KeyNotFoundException($"Track type with ID {request.TrackTypeId} not found.");

        var json = JsonSerializer.Serialize(request.Fields, new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        });

        trackType.CustomFieldDefinitions = json;
        await repo.SaveChangesAsync(ct);

        return request.Fields;
    }
}
