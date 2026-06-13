namespace Forge.Core.Models.Accounting;

/// <summary>
/// Marks the current async flow as a TRUSTED SYSTEM posting context (§5.7 SoD carve-out for background
/// jobs). The SoD boundary fail-safe-denies any GL mutation with no authenticated principal — correct for
/// HTTP requests, but Hangfire jobs (e.g. the payment-transmission settlement entry) legitimately post with
/// no principal at all. A job wraps its engine call in <c>using var _ = GlSystemPostingScope.Enter();</c>;
/// the authorizer then authorizes it as the system principal (and logs it as such).
/// <para>
/// Scope is <see cref="AsyncLocal{T}"/>-bound: it cannot leak across requests, only flows down the awaited
/// call chain that explicitly entered it, and is reset on dispose. Human-driven flows are untouched — they
/// never enter the scope, so per-user SoD capability checks apply unchanged.
/// </para>
/// </summary>
public static class GlSystemPostingScope
{
    private static readonly AsyncLocal<bool> Active = new();

    /// <summary>True when the current async flow has explicitly entered the system-posting scope.</summary>
    public static bool IsActive => Active.Value;

    /// <summary>Enter the scope; dispose to exit (use with <c>using</c>).</summary>
    public static IDisposable Enter()
    {
        Active.Value = true;
        return new ExitOnDispose();
    }

    private sealed class ExitOnDispose : IDisposable
    {
        public void Dispose() => Active.Value = false;
    }
}
