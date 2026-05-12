using MediatR;

using Forge.Core.Interfaces;

namespace Forge.Api.Features.Admin;

public record DeleteLogoCommand : IRequest;

public class DeleteLogoHandler(IStorageService storage, ISystemSettingRepository settings)
    : IRequestHandler<DeleteLogoCommand>
{
    private const string BucketName = "forge-branding";
    private const string LogoKey = "logo";

    public async Task Handle(DeleteLogoCommand request, CancellationToken ct)
    {
        var existing = await settings.FindByKeyAsync("brand.logo_content_type", ct);
        if (existing == null) return;

        try
        {
            await storage.DeleteAsync(BucketName, LogoKey, ct);
        }
        catch
        {
            // Object may not exist — ignore
        }

        await settings.UpsertAsync("brand.logo_content_type", "", "Logo removed", ct);
    }
}
