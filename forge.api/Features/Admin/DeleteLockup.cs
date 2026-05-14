using MediatR;

using Forge.Core.Interfaces;

namespace Forge.Api.Features.Admin;

public record DeleteLockupCommand(LockupKind Kind) : IRequest;

public class DeleteLockupHandler(IStorageService storage, ISystemSettingRepository settings)
    : IRequestHandler<DeleteLockupCommand>
{
    private const string BucketName = "forge-branding";

    public async Task Handle(DeleteLockupCommand request, CancellationToken ct)
    {
        var storageKey = StorageKeyFor(request.Kind);
        var contentTypeKey = $"brand.lockup_{request.Kind.ToString().ToLowerInvariant()}_content_type";

        var existing = await settings.FindByKeyAsync(contentTypeKey, ct);
        if (existing == null) return;

        try
        {
            await storage.DeleteAsync(BucketName, storageKey, ct);
        }
        catch
        {
            // Object may not exist — ignore.
        }

        await settings.UpsertAsync(contentTypeKey, "", $"Lockup {request.Kind} removed", ct);
    }

    private static string StorageKeyFor(LockupKind kind) => kind switch
    {
        LockupKind.Wordmark => "lockup-wordmark",
        LockupKind.Marquee  => "lockup-marquee",
        LockupKind.Favicon  => "lockup-favicon",
        _ => throw new ArgumentOutOfRangeException(nameof(kind)),
    };
}
