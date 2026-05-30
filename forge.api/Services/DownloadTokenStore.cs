using System.Collections.Concurrent;
using System.Security.Cryptography;

using Forge.Core.Interfaces;

namespace Forge.Api.Services;

/// <summary>
/// In-memory <see cref="IDownloadTokenStore"/>. Tokens carry 256 bits of
/// entropy (URL-safe base64) and live for <see cref="Ttl"/> before a lazy
/// sweep on the next issue discards them. A token is single-use: the first
/// <see cref="Consume"/> removes it.
///
/// Same shape as <see cref="SsoHandoffStore"/> — a process-wide
/// <see cref="ConcurrentDictionary{TKey,TValue}"/> is fine because a
/// container restart only forces in-flight installer runs (a rare event)
/// to be retried; the population is small and never persisted.
/// </summary>
public class DownloadTokenStore(IClock clock) : IDownloadTokenStore
{
    /// <summary>
    /// Window between the PS1 being downloaded and the installer actually
    /// running on the workstation. Half an hour comfortably covers download
    /// + Node install + npm install on a slow Pi or unfamiliar machine,
    /// without leaving the credential valid for weeks if the user saves
    /// the PS1 to a shared drive and forgets about it.
    /// </summary>
    private static readonly TimeSpan Ttl = TimeSpan.FromMinutes(30);

    private record Entry(int UserId, DateTimeOffset ExpiresAt);

    private static readonly ConcurrentDictionary<string, Entry> Tokens = new();

    public string Issue(int userId)
    {
        SweepExpired();
        var token = "dlt_" + Base64UrlEncode(RandomNumberGenerator.GetBytes(32));
        Tokens[token] = new Entry(userId, clock.UtcNow.Add(Ttl));
        return token;
    }

    public int? Consume(string token)
    {
        if (string.IsNullOrEmpty(token) || !Tokens.TryRemove(token, out var entry))
            return null;
        return entry.ExpiresAt > clock.UtcNow ? entry.UserId : null;
    }

    private void SweepExpired()
    {
        var now = clock.UtcNow;
        foreach (var (token, entry) in Tokens)
        {
            if (entry.ExpiresAt <= now)
                Tokens.TryRemove(token, out _);
        }
    }

    private static string Base64UrlEncode(byte[] bytes) =>
        Convert.ToBase64String(bytes)
            .TrimEnd('=')
            .Replace('+', '-')
            .Replace('/', '_');
}
