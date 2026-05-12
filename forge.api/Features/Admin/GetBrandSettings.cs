using MediatR;
using Forge.Core.Interfaces;

namespace Forge.Api.Features.Admin;

public record GetBrandSettingsQuery : IRequest<BrandSettingsResponseModel>;

public record BrandSettingsResponseModel(string? PrimaryColor, string? AccentColor, string? AppName, bool HasLogo);

public class GetBrandSettingsHandler(ISystemSettingRepository repo) : IRequestHandler<GetBrandSettingsQuery, BrandSettingsResponseModel>
{
    private static readonly string[] BrandKeys = ["theme.primary_color", "theme.accent_color", "app.name", "brand.logo_content_type"];

    public async Task<BrandSettingsResponseModel> Handle(GetBrandSettingsQuery request, CancellationToken ct)
    {
        var all = await repo.GetAllAsync(ct);
        var lookup = all.Where(s => BrandKeys.Contains(s.Key)).ToDictionary(s => s.Key, s => s.Value);

        var logoContentType = lookup.GetValueOrDefault("brand.logo_content_type");
        var hasLogo = !string.IsNullOrEmpty(logoContentType);

        return new BrandSettingsResponseModel(
            PrimaryColor: lookup.GetValueOrDefault("theme.primary_color"),
            AccentColor: lookup.GetValueOrDefault("theme.accent_color"),
            AppName: lookup.GetValueOrDefault("app.name"),
            HasLogo: hasLogo
        );
    }
}
