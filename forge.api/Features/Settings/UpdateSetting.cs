using FluentValidation;
using MediatR;

using Forge.Core.Settings;

namespace Forge.Api.Features.Settings;

/// <summary>
/// Phase 1m — single-setting upsert. The admin UI saves one field at a
/// time so an in-progress edit can't fail the rest of the group; bulk
/// save is a future convenience layer on top of this.
///
/// Empty / null value erases the row; the next read returns the
/// descriptor's DefaultValue.
/// </summary>
public record UpdateSettingCommand(string Key, string? Value) : IRequest;

public class UpdateSettingValidator : AbstractValidator<UpdateSettingCommand>
{
    public UpdateSettingValidator()
    {
        RuleFor(x => x.Key).NotEmpty().MaximumLength(200);
        RuleFor(x => x.Value).MaximumLength(8000);
    }
}

public class UpdateSettingHandler(ISettingsService settings) : IRequestHandler<UpdateSettingCommand>
{
    public Task Handle(UpdateSettingCommand request, CancellationToken cancellationToken)
        => settings.SetAsync(request.Key, request.Value, cancellationToken);
}
