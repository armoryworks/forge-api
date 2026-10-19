namespace Forge.Api.Concurrency;

/// <summary>
/// Handler-side counterpart of <see cref="IfMatchAttribute"/> for endpoints
/// whose route carries no entity id (e.g. the caller's own employee profile,
/// or a profile addressed by its user id). The middleware maps it to the same
/// 412 Precondition Failed envelope the attribute writes.
/// </summary>
public sealed class StaleVersionException(string rejectedValue)
    : Exception("Stale snapshot — record was modified by another user")
{
    public string RejectedValue { get; } = rejectedValue;

    /// <summary>
    /// Permissive when <paramref name="ifMatch"/> is absent, as the attribute
    /// is. Otherwise throws unless it names <paramref name="currentVersion"/>.
    /// </summary>
    public static void ThrowIfStale(string? ifMatch, uint currentVersion)
    {
        if (string.IsNullOrWhiteSpace(ifMatch)) return;

        var trimmed = ifMatch.Trim();
        if (trimmed.StartsWith("W/", StringComparison.Ordinal)) trimmed = trimmed[2..];
        trimmed = trimmed.Trim('"').Trim();

        if (!uint.TryParse(trimmed, out var provided) || provided != currentVersion)
            throw new StaleVersionException(ifMatch);
    }
}
