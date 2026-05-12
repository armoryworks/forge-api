using MediatR;

using Forge.Core.Interfaces;

namespace Forge.Api.Features.Admin;

public record GetLogoQuery : IRequest<GetLogoResult?>;

public record GetLogoResult(Stream Stream, string ContentType);

public class GetLogoHandler(IStorageService storage, ISystemSettingRepository settings)
    : IRequestHandler<GetLogoQuery, GetLogoResult?>
{
    private const string BucketName = "forge-branding";
    private const string LogoKey = "logo";

    public async Task<GetLogoResult?> Handle(GetLogoQuery request, CancellationToken ct)
    {
        var contentTypeSetting = await settings.FindByKeyAsync("brand.logo_content_type", ct);
        if (contentTypeSetting == null)
            return null;

        try
        {
            var stream = await storage.DownloadAsync(BucketName, LogoKey, ct);
            return new GetLogoResult(stream, contentTypeSetting.Value);
        }
        catch
        {
            return null;
        }
    }
}
