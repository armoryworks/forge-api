using MediatR;

using Forge.Core.Interfaces;

namespace Forge.Api.Features.Barcodes;

/// <summary>Read the (optional) GS1 licence configuration for GTIN allocation.</summary>
public record GetGs1SettingsQuery : IRequest<Gs1SettingsResponseModel>;

public record Gs1SettingsResponseModel(bool Configured, string? CompanyPrefix, long NextItemReference, long RemainingCapacity);

public class GetGs1SettingsHandler(ISystemSettingRepository settings)
    : IRequestHandler<GetGs1SettingsQuery, Gs1SettingsResponseModel>
{
    public async Task<Gs1SettingsResponseModel> Handle(GetGs1SettingsQuery request, CancellationToken cancellationToken)
    {
        var prefix = (await settings.FindByKeyAsync(Gs1.CompanyPrefixKey, cancellationToken))?.Value;
        prefix = string.IsNullOrWhiteSpace(prefix) ? null : prefix.Trim();
        var nextRef = long.TryParse((await settings.FindByKeyAsync(Gs1.NextItemRefKey, cancellationToken))?.Value, out var n) ? n : 1;

        long capacity = 0;
        if (prefix is not null)
        {
            var width = 12 - prefix.Length;
            capacity = width <= 0 ? 0 : Math.Max(0, (long)Math.Pow(10, width) - nextRef);
        }
        return new Gs1SettingsResponseModel(prefix is not null, prefix, nextRef, capacity);
    }
}
