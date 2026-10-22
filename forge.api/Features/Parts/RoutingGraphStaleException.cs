namespace Forge.Api.Features.Parts;

/// <summary>
/// Thrown when a whole-routing save was based on a version of the routing that someone has changed since. The
/// exception middleware maps this to 409 with problem code "routing-graph-stale" and the current version; the
/// client reloads before saving again.
/// </summary>
public class RoutingGraphStaleException(string currentVersion)
    : Exception("Someone changed this routing after you opened it. Reload it and make your changes again.")
{
    public const string Code = "routing-graph-stale";

    public string CurrentVersion { get; } = currentVersion;
}
