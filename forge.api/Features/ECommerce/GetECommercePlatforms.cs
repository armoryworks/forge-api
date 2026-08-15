using MediatR;

using Forge.Core.Enums;
using Forge.Core.Interfaces;
using Forge.Core.Models;

namespace Forge.Api.Features.ECommerce;

/// <summary>
/// The platform picker's option list, annotated with whether each one can
/// actually be polled. Driven by <see cref="IECommerceServiceFactory"/> rather
/// than by a hand-kept list, so a connector landing in DI makes its platform
/// selectable with no second edit.
/// </summary>
public record GetECommercePlatformsQuery : IRequest<List<ECommercePlatformOptionModel>>;

public class GetECommercePlatformsHandler(IECommerceServiceFactory factory)
    : IRequestHandler<GetECommercePlatformsQuery, List<ECommercePlatformOptionModel>>
{
    private static readonly HashSet<ECommercePlatform> Marketplaces =
    [
        ECommercePlatform.Amazon,
        ECommercePlatform.Ebay,
        ECommercePlatform.Etsy,
        ECommercePlatform.Walmart,
    ];

    public Task<List<ECommercePlatformOptionModel>> Handle(
        GetECommercePlatformsQuery request, CancellationToken ct)
    {
        var options = Enum.GetValues<ECommercePlatform>()
            .Select(platform => new ECommercePlatformOptionModel
            {
                Platform = platform,
                Name = platform.ToString(),
                IsSupported = factory.IsSupported(platform),
                IsMarketplace = Marketplaces.Contains(platform),
                UnavailableReason = BuildReason(platform, factory.IsSupported(platform)),
            })
            // Connectable platforms first — the list exists to be chosen from,
            // and burying the usable options under ones that are not is unkind.
            .OrderByDescending(o => o.IsSupported)
            .ThenBy(o => o.Name, StringComparer.Ordinal)
            .ToList();

        return Task.FromResult(options);
    }

    private static string? BuildReason(ECommercePlatform platform, bool isSupported)
    {
        if (isSupported) return null;

        return platform == ECommercePlatform.Manual
            ? "Manual channels have no API by design — enter their orders directly instead of polling."
            : $"No connector for {platform} is built yet. Connecting it needs a developer account and a "
              + "registered application with that platform first.";
    }
}
