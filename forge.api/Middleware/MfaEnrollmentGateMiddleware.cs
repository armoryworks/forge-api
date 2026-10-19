using System.Text.Json;

using Forge.Data.Context;
using Forge.Data.Services;

namespace Forge.Api.Middleware;

/// <summary>
/// Enforces the MFA role policy on the server. A password sign-in by a user whose role
/// requires MFA, and who has not enrolled, gets a token carrying <see cref="ClaimType"/>.
/// With that token every API call outside <see cref="AllowedPrefixes"/> answers 403 with
/// code "mfa-enrollment-required", so the user can sign out, read their own identity and
/// enrol, and nothing else. Once enrolled, <c>auth/refresh</c> issues a token without the claim.
/// </summary>
public class MfaEnrollmentGateMiddleware(RequestDelegate next)
{
    public const string ClaimType = "mfa_enrollment_required";
    public const string ErrorCode = "mfa-enrollment-required";

    private static readonly string[] AllowedPrefixes =
    [
        "/api/v1/auth",
        "/api/v1/version",
        "/api/v1/health",
    ];

    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

    /// <summary>True when the user's role requires MFA and they have not enrolled yet.</summary>
    public static async Task<bool> IsRequiredAsync(AppDbContext db, ApplicationUser user, CancellationToken ct)
        => !user.MfaEnabled && await MfaRolePolicy.IsEnforcedAsync(db, user, ct);

    /// <summary>The extra token claims for a sign-in, or null when enrolment is not required.</summary>
    public static IDictionary<string, string>? ExtraClaims(bool enrollmentRequired)
        => enrollmentRequired ? new Dictionary<string, string> { [ClaimType] = "true" } : null;

    public async Task InvokeAsync(HttpContext context)
    {
        if (context.User.FindFirst(ClaimType)?.Value != "true"
            || !context.Request.Path.StartsWithSegments("/api", StringComparison.OrdinalIgnoreCase)
            || AllowedPrefixes.Any(p => context.Request.Path.StartsWithSegments(p, StringComparison.OrdinalIgnoreCase)))
        {
            await next(context);
            return;
        }

        context.Response.StatusCode = StatusCodes.Status403Forbidden;
        context.Response.ContentType = "application/problem+json";
        await context.Response.WriteAsync(JsonSerializer.Serialize(new
        {
            status = 403,
            title = "MFA enrolment required",
            detail = "Your role requires multi-factor authentication. Set it up before using Forge.",
            type = "about:blank",
            code = ErrorCode,
        }, JsonOptions));
    }
}
