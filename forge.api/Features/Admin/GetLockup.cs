using MediatR;
using Microsoft.AspNetCore.Hosting;

using Forge.Core.Interfaces;

namespace Forge.Api.Features.Admin;

public enum LockupKind { Wordmark, Marquee, Favicon }
public enum LockupTheme { Dark, Light }

public record GetLockupQuery(LockupKind Kind, LockupTheme Theme) : IRequest<GetLockupResult?>;

public record GetLockupResult(Stream Stream, string ContentType);

/// <summary>
/// Serves a branding lockup. First checks MinIO for an admin-uploaded custom
/// file; if none, falls back to the bundled Forge default SVG in wwwroot.
/// The Theme parameter only affects the default-fallback path — admin uploads
/// are served as-is regardless of theme (single-upload model for now).
/// </summary>
public class GetLockupHandler(
    IStorageService storage,
    ISystemSettingRepository settings,
    IWebHostEnvironment env)
    : IRequestHandler<GetLockupQuery, GetLockupResult?>
{
    private const string BucketName = "forge-branding";

    // Default filenames bundled in wwwroot/branding-defaults/ — these are the
    // canonical Forge lockups shipped with the app. Admins override by
    // uploading via POST /admin/branding/{kind}.
    private const string DefaultWordmarkDark  = "forge-wordmark.svg";
    private const string DefaultWordmarkLight = "forge-wordmark-light.svg";
    private const string DefaultMarqueeDark   = "forge-marquee.svg";
    private const string DefaultMarqueeLight  = "forge-marquee-light.svg";
    private const string DefaultFavicon       = "forge-favicon.svg";

    public async Task<GetLockupResult?> Handle(GetLockupQuery request, CancellationToken ct)
    {
        var storageKey = StorageKeyFor(request.Kind);
        var contentTypeKey = $"brand.lockup_{request.Kind.ToString().ToLowerInvariant()}_content_type";

        // 1. Custom admin upload wins.
        var contentTypeSetting = await settings.FindByKeyAsync(contentTypeKey, ct);
        if (contentTypeSetting != null && !string.IsNullOrWhiteSpace(contentTypeSetting.Value))
        {
            try
            {
                var stream = await storage.DownloadAsync(BucketName, storageKey, ct);
                return new GetLockupResult(stream, contentTypeSetting.Value);
            }
            catch
            {
                // Setting says we have an upload but storage doesn't — fall through to default.
            }
        }

        // 2. Default-fallback: serve the bundled Forge SVG from wwwroot.
        var defaultFileName = DefaultFileFor(request.Kind, request.Theme);
        var defaultPath = Path.Combine(env.ContentRootPath, "wwwroot", "branding-defaults", defaultFileName);
        if (!File.Exists(defaultPath))
            return null;

        var defaultStream = new FileStream(defaultPath, FileMode.Open, FileAccess.Read, FileShare.Read);
        return new GetLockupResult(defaultStream, "image/svg+xml");
    }

    private static string StorageKeyFor(LockupKind kind) => kind switch
    {
        LockupKind.Wordmark => "lockup-wordmark",
        LockupKind.Marquee  => "lockup-marquee",
        LockupKind.Favicon  => "lockup-favicon",
        _ => throw new ArgumentOutOfRangeException(nameof(kind)),
    };

    private static string DefaultFileFor(LockupKind kind, LockupTheme theme) => (kind, theme) switch
    {
        (LockupKind.Wordmark, LockupTheme.Dark)  => DefaultWordmarkDark,
        (LockupKind.Wordmark, LockupTheme.Light) => DefaultWordmarkLight,
        (LockupKind.Marquee,  LockupTheme.Dark)  => DefaultMarqueeDark,
        (LockupKind.Marquee,  LockupTheme.Light) => DefaultMarqueeLight,
        // Favicon ignores theme — the same shield works on dark or light tabs.
        (LockupKind.Favicon,  _)                 => DefaultFavicon,
        _ => throw new ArgumentOutOfRangeException(nameof(kind)),
    };
}
