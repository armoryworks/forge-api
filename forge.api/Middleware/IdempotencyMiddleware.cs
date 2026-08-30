using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;

using Microsoft.EntityFrameworkCore;

using Forge.Core.Entities;
using Forge.Core.Interfaces;
using Forge.Data.Context;

namespace Forge.Api.Middleware;

/// <summary>
/// Idempotent mutations for clients that retry (the mobile app's offline
/// queue above all). A POST/PUT/PATCH/DELETE carrying an Idempotency-Key is
/// executed once; a replay of the same key inside 24 hours gets the stored
/// status + body back without re-running the handler. The same key with a
/// different request body is refused (422). Keys are scoped to the caller —
/// user id, shared device id, or the anonymous bucket — so they never
/// collide across principals. Only responses below 500 are stored: a
/// transient server failure should be retried for real.
/// </summary>
public class IdempotencyMiddleware(RequestDelegate next, ILogger<IdempotencyMiddleware> logger)
{
    public const string HeaderName = "Idempotency-Key";
    private static readonly TimeSpan Retention = TimeSpan.FromHours(24);
    private static readonly HashSet<string> MutatingMethods = ["POST", "PUT", "PATCH", "DELETE"];

    public async Task InvokeAsync(HttpContext context, AppDbContext db, IClock clock)
    {
        if (!MutatingMethods.Contains(context.Request.Method)
            || !context.Request.Headers.TryGetValue(HeaderName, out var keyValues)
            || string.IsNullOrWhiteSpace(keyValues))
        {
            await next(context);
            return;
        }

        var key = keyValues.ToString().Trim();
        if (key.Length > 80)
        {
            context.Response.StatusCode = StatusCodes.Status400BadRequest;
            await context.Response.WriteAsync("Idempotency-Key too long.");
            return;
        }

        var scope = ResolveScope(context);
        var fingerprint = await FingerprintAsync(context);
        var now = clock.UtcNow;

        var existing = await db.IdempotencyKeys.AsNoTracking()
            .FirstOrDefaultAsync(k => k.Scope == scope && k.Key == key && k.ExpiresAt > now,
                context.RequestAborted);

        if (existing is not null)
        {
            if (existing.RequestFingerprint != fingerprint)
            {
                context.Response.StatusCode = StatusCodes.Status422UnprocessableEntity;
                context.Response.ContentType = "application/problem+json";
                await context.Response.WriteAsync(
                    """{"status":422,"title":"Idempotency-Key reused for a different request","type":"about:blank","code":"idempotency-key-mismatch"}""");
                return;
            }

            logger.LogInformation("Idempotent replay for {Scope} key {Key} → {Status}", scope, key, existing.StatusCode);
            context.Response.StatusCode = existing.StatusCode;
            context.Response.Headers["Idempotent-Replayed"] = "true";
            if (existing.ContentType is not null) context.Response.ContentType = existing.ContentType;
            if (existing.ResponseBody is not null) await context.Response.WriteAsync(existing.ResponseBody);
            return;
        }

        var originalBody = context.Response.Body;
        await using var buffer = new MemoryStream();
        context.Response.Body = buffer;

        try
        {
            await next(context);

            buffer.Position = 0;
            var body = await new StreamReader(buffer, Encoding.UTF8).ReadToEndAsync(context.RequestAborted);

            if (context.Response.StatusCode < 500)
            {
                db.IdempotencyKeys.Add(new IdempotencyKey
                {
                    Scope = scope,
                    Key = key,
                    RequestFingerprint = fingerprint,
                    StatusCode = context.Response.StatusCode,
                    ContentType = context.Response.ContentType,
                    ResponseBody = body,
                    CreatedAt = now,
                    ExpiresAt = now + Retention,
                });
                // A raced duplicate loses on the unique (scope, key) index; the
                // winner's row is the one that matters.
                try { await db.SaveChangesAsync(context.RequestAborted); }
                catch (DbUpdateException) { /* duplicate in flight — keep the first */ }

                await db.IdempotencyKeys
                    .Where(k => k.ExpiresAt < now - Retention)
                    .ExecuteDeleteAsync(context.RequestAborted);
            }

            buffer.Position = 0;
            await buffer.CopyToAsync(originalBody, context.RequestAborted);
        }
        finally
        {
            context.Response.Body = originalBody;
        }
    }

    private static string ResolveScope(HttpContext context)
    {
        var userId = context.User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (!string.IsNullOrEmpty(userId)) return $"user:{userId}";
        if (context.Items[SharedDeviceMiddleware.ItemKey] is int deviceId) return $"device:{deviceId}";
        return $"anon:{context.Connection.RemoteIpAddress}";
    }

    private static async Task<string> FingerprintAsync(HttpContext context)
    {
        context.Request.EnableBuffering();
        using var reader = new StreamReader(context.Request.Body, Encoding.UTF8, leaveOpen: true);
        var body = await reader.ReadToEndAsync(context.RequestAborted);
        context.Request.Body.Position = 0;

        var material = $"{context.Request.Method} {context.Request.Path}{context.Request.QueryString}\n{body}";
        return Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(material)));
    }
}
