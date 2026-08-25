using Microsoft.Extensions.Caching.Memory;

using Forge.Core.Interfaces;

namespace Forge.Api.Services;

public class ScanCollapseService(
    IMemoryCache cache,
    IClock clock,
    ILogger<ScanCollapseService> logger) : IScanCollapseService
{
    private static readonly TimeSpan Window = TimeSpan.FromSeconds(3);

    public bool IsDuplicate(string deviceKey, string code, string action)
    {
        var cacheKey = $"scan:{deviceKey}:{action}:{code}";
        var now = clock.UtcNow;

        if (cache.TryGetValue<DateTimeOffset>(cacheKey, out var last) && now - last < Window)
        {
            logger.LogInformation(
                "Collapsed duplicate scan {Action} of {Code} from {Device} ({Ms} ms after the first)",
                action, code, deviceKey, (now - last).TotalMilliseconds);
            return true;
        }

        cache.Set(cacheKey, now, Window);
        return false;
    }
}
