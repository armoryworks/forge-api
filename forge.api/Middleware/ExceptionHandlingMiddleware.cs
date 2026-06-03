using System.Security.Authentication;
using System.Text.Json;
using FluentValidation;
using Microsoft.AspNetCore.Mvc;

using Forge.Api.Capabilities;
using Forge.Api.Features.Auth;
using Forge.Api.Workflows;

namespace Forge.Api.Middleware;

public class ExceptionHandlingMiddleware(RequestDelegate next, ILogger<ExceptionHandlingMiddleware> logger)
{
    public async Task InvokeAsync(HttpContext context)
    {
        try
        {
            await next(context);
        }
        catch (ValidationException ex)
        {
            context.Response.StatusCode = StatusCodes.Status400BadRequest;
            context.Response.ContentType = "application/problem+json";

            var problem = new ValidationProblemDetails(
                ex.Errors
                    .GroupBy(e => e.PropertyName)
                    .ToDictionary(
                        g => g.Key,
                        g => g.Select(e => e.ErrorMessage).ToArray()))
            {
                Status = StatusCodes.Status400BadRequest,
                Title = "Validation failed",
                Type = "about:blank"
            };

            await context.Response.WriteAsJsonAsync(problem);
        }
        catch (KeyNotFoundException ex)
        {
            context.Response.StatusCode = StatusCodes.Status404NotFound;
            context.Response.ContentType = "application/problem+json";

            var problem = new ProblemDetails
            {
                Status = StatusCodes.Status404NotFound,
                Title = "Not found",
                Detail = ex.Message,
                Type = "about:blank"
            };

            await context.Response.WriteAsJsonAsync(problem);
        }
        catch (WorkflowMissingValidatorsException ex)
        {
            // Workflow Pattern Phase 3 — readiness gate failure. Returns 409
            // with the envelope { title, detail, missing: [...] } so the
            // client can render "Missing: BOM, Routing" with jump-to links.
            logger.LogInformation("[WORKFLOW] Readiness gate failed: {Count} missing validators", ex.Missing.Count);

            context.Response.StatusCode = StatusCodes.Status409Conflict;
            context.Response.ContentType = "application/problem+json";

            var envelope = new
            {
                status = StatusCodes.Status409Conflict,
                title = "Readiness validators not satisfied",
                detail = ex.Message,
                type = "about:blank",
                code = "workflow-readiness-missing",
                missing = ex.Missing,
            };
            var json = JsonSerializer.Serialize(envelope, new JsonSerializerOptions
            {
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            });
            await context.Response.WriteAsync(json);
        }
        catch (InvalidOperationException ex) when (IsFrameworkException(ex))
        {
            logger.LogError(ex, "Framework InvalidOperationException (e.g. EF Core query translation) — returning 500");
            context.Response.StatusCode = StatusCodes.Status500InternalServerError;
            context.Response.ContentType = "application/problem+json";

            var problem = new ProblemDetails
            {
                Status = StatusCodes.Status500InternalServerError,
                Title = "An error occurred",
                Type = "about:blank"
            };

            await context.Response.WriteAsJsonAsync(problem);
        }
        catch (InvalidOperationException ex)
        {
            logger.LogWarning(ex, "InvalidOperationException caught — returning 409");
            context.Response.StatusCode = StatusCodes.Status409Conflict;
            context.Response.ContentType = "application/problem+json";

            var problem = new ProblemDetails
            {
                Status = StatusCodes.Status409Conflict,
                Title = "Conflict",
                Detail = ex.Message,
                Type = "about:blank"
            };

            await context.Response.WriteAsJsonAsync(problem);
        }
        catch (CapabilityDisabledException ex)
        {
            // Mirror the CapabilityGateMiddleware envelope shape so HTTP callers
            // see the same 403 + X-Capability-Disabled response regardless of
            // whether the controller-edge gate or the MediatR pipeline gate
            // fired (Phase 4 Phase-H).
            logger.LogInformation(
                "[CAPABILITY-GATE] MediatR request rejected — capability {Capability} disabled",
                ex.Capability);

            context.Response.StatusCode = StatusCodes.Status403Forbidden;
            context.Response.ContentType = "application/problem+json";
            context.Response.Headers["X-Capability-Disabled"] = ex.Capability;

            var json = JsonSerializer.Serialize(ex.ToEnvelope(), new JsonSerializerOptions
            {
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            });
            await context.Response.WriteAsync(json);
        }
        catch (ForbiddenException ex)
        {
            // Authenticated but not permitted to act on this specific resource
            // (per-row ownership check). 403 — distinct from the 401 below.
            context.Response.StatusCode = StatusCodes.Status403Forbidden;
            context.Response.ContentType = "application/problem+json";
            await context.Response.WriteAsJsonAsync(new ProblemDetails
            {
                Status = StatusCodes.Status403Forbidden,
                Title = "Forbidden",
                Detail = ex.Message,
                Type = "about:blank",
            });
        }
        catch (UnauthorizedAccessException ex)
        {
            context.Response.StatusCode = StatusCodes.Status401Unauthorized;
            context.Response.ContentType = "application/problem+json";

            var problem = new ProblemDetails
            {
                Status = StatusCodes.Status401Unauthorized,
                Title = "Unauthorized",
                Detail = ex.Message,
                Type = "about:blank"
            };

            await context.Response.WriteAsJsonAsync(problem);
        }
        catch (AuthenticationException ex)
        {
            // External-token validation failures (e.g. bad Google id_token
            // in the SSO token-exchange flow). The caller IS unauthenticated
            // — they presented a credential that didn't verify — so 401 is
            // the correct status. Message goes back verbatim so the caller
            // can distinguish "expired" / "wrong audience" / "bad signature"
            // when debugging; no internal details surface.
            logger.LogWarning(ex, "External authentication failed — returning 401");

            context.Response.StatusCode = StatusCodes.Status401Unauthorized;
            context.Response.ContentType = "application/problem+json";

            var problem = new ProblemDetails
            {
                Status = StatusCodes.Status401Unauthorized,
                Title = "Authentication failed",
                Detail = ex.Message,
                Type = "about:blank"
            };

            await context.Response.WriteAsJsonAsync(problem);
        }
        catch (SsoDomainNotPermittedException ex)
        {
            // The provider authenticated the user successfully, but this
            // install's per-provider AllowedDomains policy excludes their
            // email domain. 403 is the correct status (authenticated but
            // not authorized).
            logger.LogInformation(
                "[SSO] {Provider}: domain '{Domain}' not in AllowedDomains — returning 403",
                ex.Provider, ex.EmailDomain);

            context.Response.StatusCode = StatusCodes.Status403Forbidden;
            context.Response.ContentType = "application/problem+json";

            var problem = new ProblemDetails
            {
                Status = StatusCodes.Status403Forbidden,
                Title = "Domain not permitted",
                Detail = ex.Message,
                Type = "about:blank"
            };

            await context.Response.WriteAsJsonAsync(problem);
        }
        catch (Forge.Core.Models.Accounting.PostingException ex)
        {
            // Accounting GL Phase-0 — a posting-engine validation failure
            // (unbalanced entry, unmapped/non-postable/cross-book account,
            // control line missing a party, etc.). These are caller-input
            // failures, so 400 with the machine-readable Code surfaced for the
            // client. (Reached only via the CAP-ACCT-FULLGL-gated GL endpoints —
            // dark while the capability is OFF.)
            logger.LogWarning(ex, "[ACCT-GL] Posting rejected: {Code}", ex.Code);

            context.Response.StatusCode = StatusCodes.Status400BadRequest;
            context.Response.ContentType = "application/problem+json";

            var problem = new ProblemDetails
            {
                Status = StatusCodes.Status400BadRequest,
                Title = "Posting rejected",
                Detail = ex.Message,
                Type = "about:blank",
            };
            problem.Extensions["code"] = ex.Code;

            await context.Response.WriteAsJsonAsync(problem);
        }
        catch (Forge.Core.Models.Accounting.GlAuthorizationException ex)
        {
            // Accounting GL §5.7 — segregation-of-duties denial at the posting-
            // engine boundary: the caller lacks the required GL capability. This
            // is an authorization failure (not caller-input), so 403. (Reached
            // only via the CAP-ACCT-FULLGL-gated GL endpoints — dark while the
            // capability is OFF.)
            logger.LogWarning("[ACCT-GL] SoD denial: missing {Capability}", ex.RequiredCapability);

            context.Response.StatusCode = StatusCodes.Status403Forbidden;
            context.Response.ContentType = "application/problem+json";

            var problem = new ProblemDetails
            {
                Status = StatusCodes.Status403Forbidden,
                Title = "GL operation not permitted",
                Detail = ex.Message,
                Type = "about:blank",
            };
            problem.Extensions["requiredCapability"] = ex.RequiredCapability.ToString();

            await context.Response.WriteAsJsonAsync(problem);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Unhandled exception");

            context.Response.StatusCode = StatusCodes.Status500InternalServerError;
            context.Response.ContentType = "application/problem+json";

            var problem = new ProblemDetails
            {
                Status = StatusCodes.Status500InternalServerError,
                Title = "An error occurred",
                Type = "about:blank"
            };

            await context.Response.WriteAsJsonAsync(problem);
        }
    }

    // Business handlers throw InvalidOperationException with user-readable messages
    // (e.g. "Cannot delete order with active shipments"). Framework code — EF Core
    // query translation, LINQ provider issues, JSON serialization — also throws
    // InvalidOperationException but with internal details that must not leak to
    // users. Distinguish by stack origin + well-known EF Core message prefixes.
    private static bool IsFrameworkException(InvalidOperationException ex)
    {
        var source = ex.Source ?? string.Empty;
        if (source.StartsWith("Microsoft.EntityFrameworkCore", StringComparison.Ordinal) ||
            source.StartsWith("Microsoft.AspNetCore", StringComparison.Ordinal) ||
            source.StartsWith("System.Text.Json", StringComparison.Ordinal))
        {
            return true;
        }

        var message = ex.Message ?? string.Empty;
        return message.Contains("could not be translated", StringComparison.Ordinal)
            || message.Contains("The LINQ expression", StringComparison.Ordinal);
    }
}
