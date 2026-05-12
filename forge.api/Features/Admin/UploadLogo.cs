using MediatR;

using Forge.Core.Interfaces;

namespace Forge.Api.Features.Admin;

public record UploadLogoCommand(Stream FileStream, string ContentType) : IRequest<string>;

public class UploadLogoHandler(IStorageService storage, ISystemSettingRepository settings)
    : IRequestHandler<UploadLogoCommand, string>
{
    private const string BucketName = "forge-branding";
    private const string LogoKey = "logo";

    public async Task<string> Handle(UploadLogoCommand request, CancellationToken ct)
    {
        await storage.EnsureBucketExistsAsync(BucketName, ct);
        await storage.UploadAsync(BucketName, LogoKey, request.FileStream, request.ContentType, ct);

        await settings.UpsertAsync("brand.logo_content_type", request.ContentType, "Logo content type", ct);

        return LogoKey;
    }
}
