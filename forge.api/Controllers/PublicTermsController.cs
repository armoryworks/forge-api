using System.Net;

using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

using Forge.Api.Capabilities;
using Forge.Api.Features.Terms;

namespace Forge.Api.Controllers;

/// <summary>
/// S3 — anonymous "view full terms" page behind the unguessable snapshot
/// access token emailed with a quote. Server-rendered minimal HTML (inline
/// styles, no app shell) so the recipient needs no login and no SPA.
///
/// Rate limiting: no named per-endpoint policies exist in this app — the
/// global partitioned limiter in Program.cs (fixed window per user/IP,
/// no-limit only for hubs/health/version/dev + loopback) already covers this
/// route, so nothing extra is attached here.
///
/// 404 semantics: unknown or soft-deleted tokens throw KeyNotFoundException
/// in the handler, which the exception middleware maps to 404.
/// </summary>
[ApiController]
[Route("api/v1/public/terms")]
[AllowAnonymous]
[RequiresCapability("CAP-O2C-QUOTE")]
public class PublicTermsController(IMediator mediator) : ControllerBase
{
    [HttpGet("{token}")]
    public async Task<ContentResult> GetTerms(string token)
    {
        var result = await mediator.Send(new GetPublicTermsQuery(token));

        var quoteNumber = WebUtility.HtmlEncode(result.QuoteNumber);
        // CompiledHtml is generated exclusively by TermsCompilationService,
        // which HTML-encodes all author content — safe to embed raw.
        var html = $"""
            <!doctype html>
            <html lang="en">
            <head>
              <meta charset="utf-8">
              <meta name="viewport" content="width=device-width, initial-scale=1">
              <meta name="robots" content="noindex">
              <title>Terms &amp; Conditions — Quote {quoteNumber}</title>
            </head>
            <body style="margin:0;padding:0;background:#f5f5f5;font-family:Segoe UI,Arial,sans-serif;color:#212121;">
              <div style="max-width:760px;margin:24px auto;padding:32px;background:#ffffff;border:1px solid #e0e0e0;border-radius:8px;">
                <h1 style="margin:0 0 4px 0;font-size:22px;color:#1565c0;">Terms &amp; Conditions</h1>
                <p style="margin:0 0 24px 0;color:#616161;">
                  Quote {quoteNumber} &middot; sent {result.SentAt:MM/dd/yyyy}
                </p>
                {result.CompiledHtml}
                <p style="margin-top:32px;font-size:12px;color:#9e9e9e;">
                  This page shows the terms exactly as compiled when the quote was sent.
                </p>
              </div>
            </body>
            </html>
            """;

        return Content(html, "text/html; charset=utf-8");
    }
}
