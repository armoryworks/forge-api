using FluentValidation;
using MediatR;

using Forge.Core.Interfaces;

namespace Forge.Api.Features.Barcodes;

/// <summary>Set (or clear) the licensed GS1 company prefix used to allocate GTINs. Admin-only.</summary>
public record UpdateGs1SettingsCommand(string? CompanyPrefix) : IRequest;

public class UpdateGs1SettingsValidator : AbstractValidator<UpdateGs1SettingsCommand>
{
    public UpdateGs1SettingsValidator()
    {
        // Empty clears the prefix (revert to internal-only); otherwise it must be a valid GS1 prefix shape.
        RuleFor(x => x.CompanyPrefix)
            .Must(p => string.IsNullOrWhiteSpace(p) || (Gs1.IsAllDigits(p.Trim()) && p.Trim().Length is >= 6 and <= 11))
            .WithMessage("GS1 company prefix must be 6–11 digits.");
    }
}

public class UpdateGs1SettingsHandler(ISystemSettingRepository settings) : IRequestHandler<UpdateGs1SettingsCommand>
{
    public async Task Handle(UpdateGs1SettingsCommand request, CancellationToken cancellationToken)
    {
        var prefix = string.IsNullOrWhiteSpace(request.CompanyPrefix) ? "" : request.CompanyPrefix.Trim();
        await settings.UpsertAsync(Gs1.CompanyPrefixKey, prefix, "Licensed GS1 company prefix for GTIN allocation", cancellationToken);

        // Seed the allocation counter on first configuration; never reset it once set.
        if (await settings.FindByKeyAsync(Gs1.NextItemRefKey, cancellationToken) is null)
            await settings.UpsertAsync(Gs1.NextItemRefKey, "1", "Next GS1 item reference to allocate", cancellationToken);

        await settings.SaveChangesAsync(cancellationToken);
    }
}
