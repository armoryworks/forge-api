using FluentValidation;
using MediatR;

using Forge.Core.Interfaces;
using Forge.Core.Models;

namespace Forge.Api.Features.AutoPo;

public record UpdateAutoPoSettingsCommand(
    bool? Enabled,
    string? DefaultMode,
    int? BufferDays,
    bool? NotifyChat) : IRequest<AutoPoSettingsResponseModel>;

public class UpdateAutoPoSettingsValidator : AbstractValidator<UpdateAutoPoSettingsCommand>
{
    private static readonly string[] ValidModes = ["Suggest", "Draft", "Automatic"];

    public UpdateAutoPoSettingsValidator()
    {
        When(x => x.DefaultMode is not null, () =>
        {
            RuleFor(x => x.DefaultMode)
                .Must(m => ValidModes.Contains(m!))
                .WithMessage("Mode must be one of: Suggest, Draft, Automatic");
        });

        When(x => x.BufferDays.HasValue, () =>
        {
            RuleFor(x => x.BufferDays!.Value)
                .InclusiveBetween(0, 30)
                .WithMessage("Buffer days must be between 0 and 30");
        });
    }
}

public class UpdateAutoPoSettingsHandler(
    ISystemSettingRepository settings,
    IMediator mediator) : IRequestHandler<UpdateAutoPoSettingsCommand, AutoPoSettingsResponseModel>
{
    public async Task<AutoPoSettingsResponseModel> Handle(UpdateAutoPoSettingsCommand request, CancellationToken ct)
    {
        if (request.Enabled.HasValue)
            await settings.UpsertAsync("inventory:auto_po_enabled", request.Enabled.Value.ToString(), "Auto-PO master switch", ct);

        if (request.DefaultMode is not null)
            await settings.UpsertAsync("inventory:auto_po_mode", request.DefaultMode, "Default auto-PO mode (Suggest/Draft/Automatic)", ct);

        if (request.BufferDays.HasValue)
            await settings.UpsertAsync("inventory:auto_po_buffer_days", request.BufferDays.Value.ToString(), "Buffer days before needed-by date", ct);

        if (request.NotifyChat.HasValue)
            await settings.UpsertAsync("inventory:auto_po_notify_chat", request.NotifyChat.Value.ToString(), "Send chat notification for auto-PO actions", ct);

        await settings.SaveChangesAsync(ct);

        return await mediator.Send(new GetAutoPoSettingsQuery(), ct);
    }
}
