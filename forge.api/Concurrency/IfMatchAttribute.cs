using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.EntityFrameworkCore;
using Forge.Core.Entities;
using Forge.Data.Context;

namespace Forge.Api.Concurrency;

/// <summary>
/// Phase 3 / WU-11 / TODO E1 — optimistic locking for transactional entities.
///
/// Action filter applied to PATCH/PUT/DELETE endpoints on transactional
/// resources (Job, Invoice, PurchaseOrder, Payment, Shipment, SalesOrder,
/// Quote). Behaviour:
///
///   • If the request carries an If-Match header, the filter loads the entity's
///     current Version and compares. Mismatch → 412 Precondition Failed with
///     the WU-02 error envelope.
///   • If no If-Match header is present, the filter is permissive (no check).
///     This preserves backward compatibility with existing UI requests.
///     Flagged as a follow-up: tighten to STRICT once UI clients reliably
///     send If-Match.
///   • On a successful response, the result filter writes the entity's current
///     Version to the ETag response header so the client can cache it for the
///     next mutating request.
///
/// Cases: CONC-OPTIMISTIC-LOCK-001.
///
/// Usage: [IfMatch(typeof(Job), "id")] on the controller action. The
/// routeParam names the route value carrying the entity ID (defaults to "id").
/// </summary>
[AttributeUsage(AttributeTargets.Method, AllowMultiple = false)]
public sealed class IfMatchAttribute : Attribute, IAsyncActionFilter
{
    public Type EntityType { get; }
    public string RouteParam { get; }

    public IfMatchAttribute(Type entityType, string routeParam = "id")
    {
        if (!typeof(IConcurrencyVersioned).IsAssignableFrom(entityType))
            throw new ArgumentException(
                $"{entityType.Name} must implement IConcurrencyVersioned to be used with [IfMatch].",
                nameof(entityType));
        EntityType = entityType;
        RouteParam = routeParam;
    }

    public async Task OnActionExecutionAsync(ActionExecutingContext context, ActionExecutionDelegate next)
    {
        var http = context.HttpContext;
        var db = http.RequestServices.GetRequiredService<AppDbContext>();

        // Resolve entity ID from route data
        if (!context.RouteData.Values.TryGetValue(RouteParam, out var idObj) || idObj is null)
        {
            await next();
            return;
        }
        if (!int.TryParse(idObj.ToString(), out var id))
        {
            await next();
            return;
        }

        // Read If-Match header (permissive: skip check if absent)
        string? ifMatchRaw = http.Request.Headers.IfMatch.ToString();
        if (!string.IsNullOrWhiteSpace(ifMatchRaw))
        {
            // ETag values may be quoted: "42" — strip quotes & weak prefix.
            var ifMatch = ifMatchRaw.Trim();
            if (ifMatch.StartsWith("W/", StringComparison.Ordinal)) ifMatch = ifMatch[2..];
            ifMatch = ifMatch.Trim('"').Trim();

            var current = await ConcurrencyLookup.LoadVersionAsync(db, EntityType, id, http.RequestAborted);

            // If the entity does not exist, we let the action run — it will
            // handle the not-found case with its own 404 logic.
            if (current is not null)
            {
                if (!uint.TryParse(ifMatch, out var providedVersion) || providedVersion != current.Value)
                {
                    context.Result = new ObjectResult(new
                    {
                        errors = new[]
                        {
                            new
                            {
                                field = "If-Match",
                                message = "Stale snapshot — record was modified by another user",
                                rejectedValue = ifMatchRaw
                            }
                        }
                    })
                    {
                        StatusCode = StatusCodes.Status412PreconditionFailed
                    };
                    return;
                }
            }
        }

        // Proceed with the action.
        var executed = await next();

        // After action — set ETag header reflecting the current version.
        if (executed.Exception is null)
        {
            var statusCode = executed.HttpContext.Response.StatusCode;
            if (statusCode is >= 200 and < 300)
            {
                var newVersion = await ConcurrencyLookup.LoadVersionAsync(db, EntityType, id, http.RequestAborted);
                if (newVersion is not null)
                {
                    executed.HttpContext.Response.Headers.ETag = $"\"{newVersion.Value}\"";
                }
            }
        }
    }
}
