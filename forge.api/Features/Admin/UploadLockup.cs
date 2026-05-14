using MediatR;

using Forge.Core.Interfaces;

namespace Forge.Api.Features.Admin;

public record UploadLockupCommand(LockupKind Kind, Stream FileStream, string ContentType) : IRequest<string>;

public class UploadLockupHandler(IStorageService storage, ISystemSettingRepository settings)
    : IRequestHandler<UploadLockupCommand, string>
{
    private const string BucketName = "forge-branding";

    public async Task<string> Handle(UploadLockupCommand request, CancellationToken ct)
    {
        var storageKey = StorageKeyFor(request.Kind);
        var contentTypeKey = $"brand.lockup_{request.Kind.ToString().ToLowerInvariant()}_content_type";

        await storage.EnsureBucketExistsAsync(BucketName, ct);
        await storage.UploadAsync(BucketName, storageKey, request.FileStream, request.ContentType, ct);

        await settings.UpsertAsync(contentTypeKey, request.ContentType, $"Lockup {request.Kind} content type", ct);

        return storageKey;
    }

    private static string StorageKeyFor(LockupKind kind) => kind switch
    {
        LockupKind.Wordmark => "lockup-wordmark",
        LockupKind.Marquee  => "lockup-marquee",
        LockupKind.Favicon  => "lockup-favicon",
        _ => throw new ArgumentOutOfRangeException(nameof(kind)),
    };
}
