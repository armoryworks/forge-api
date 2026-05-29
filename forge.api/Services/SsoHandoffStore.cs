using System.Collections.Concurrent;
using System.Security.Cryptography;

using Forge.Api.Features.Auth;
using Forge.Core.Interfaces;

namespace Forge.Api.Services;

/// <summary>
/// In-memory implementation of <see cref="ISsoHandoffStore"/>. Codes carry 256
/// bits of entropy (URL-safe base64) and live for <see cref="Ttl"/> before a
/// lazy sweep on the next access discards them. A code is single-use: the first
/// <see cref="Consume"/> removes it.
/// </summary>
public class SsoHandoffStore(IClock clock) : ISsoHandoffStore
{
    /// <summary>
    /// Window between the callback redirect and the SPA's exchange POST. Kept
    /// tight — the round-trip is a same-tab redirect followed immediately by a
    /// fetch, so seconds is generous.
    /// </summary>
    private static readonly TimeSpan Ttl = TimeSpan.FromSeconds(60);

    private record Entry(LoginResponse Response, DateTimeOffset ExpiresAt);

    private static readonly ConcurrentDictionary<string, Entry> Codes = new();

    public string Create(LoginResponse response)
    {
        SweepExpired();

        var code = Base64UrlEncode(RandomNumberGenerator.GetBytes(32));
        Codes[code] = new Entry(response, clock.UtcNow.Add(Ttl));
        return code;
    }

    public LoginResponse? Consume(string code)
    {
        if (string.IsNullOrEmpty(code) || !Codes.TryRemove(code, out var entry))
            return null;

        return entry.ExpiresAt > clock.UtcNow ? entry.Response : null;
    }

    private void SweepExpired()
    {
        var now = clock.UtcNow;
        foreach (var (code, entry) in Codes)
        {
            if (entry.ExpiresAt <= now)
                Codes.TryRemove(code, out _);
        }
    }

    private static string Base64UrlEncode(byte[] bytes) =>
        Convert.ToBase64String(bytes)
            .TrimEnd('=')
            .Replace('+', '-')
            .Replace('/', '_');
}
