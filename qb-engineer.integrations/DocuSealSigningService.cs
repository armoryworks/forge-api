using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;

using Microsoft.Extensions.Logging;

using QBEngineer.Core.Interfaces;
using QBEngineer.Core.Models;
using QBEngineer.Core.Settings;

namespace QBEngineer.Integrations;

/// <summary>
/// Real implementation of <see cref="IDocumentSigningService"/> backed by
/// the DocuSeal HTTP API.
///
/// Phase 1m: BaseUrl + ApiKey + PublicBaseUrl + Timeout read live from
/// <see cref="ISettingsService"/> at request time. Each method composes
/// a full URL + sets the X-Auth-Token header per-call rather than
/// pinning HttpClient defaults at construction (HttpClient.BaseAddress
/// is immutable after first use, which would freeze admin config
/// changes until restart).
/// </summary>
public class DocuSealSigningService(
    HttpClient httpClient,
    ISettingsService settings,
    ILogger<DocuSealSigningService> logger) : IDocumentSigningService
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    public async Task<bool> IsAvailableAsync(CancellationToken ct)
    {
        try
        {
            using var req = await BuildRequestAsync(HttpMethod.Get, "/api/templates", ct);
            using var response = await httpClient.SendAsync(req, ct);
            return response.IsSuccessStatusCode;
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "DocuSeal health check failed");
            return false;
        }
    }

    public async Task<int> CreateTemplateFromPdfAsync(string name, byte[] pdfBytes, CancellationToken ct)
    {
        if (pdfBytes.Length == 0)
            return await CreateBlankTemplateAsync(name, ct);

        logger.LogInformation("DocuSeal CreateTemplate: {Name} ({Size} bytes)", name, pdfBytes.Length);

        using var content = new MultipartFormDataContent();
        content.Add(new ByteArrayContent(pdfBytes), "files[]", $"{name}.pdf");
        content.Add(new StringContent(name), "name");

        using var req = await BuildRequestAsync(HttpMethod.Post, "/api/templates/pdf", ct);
        req.Content = content;
        using var response = await httpClient.SendAsync(req, ct);
        response.EnsureSuccessStatusCode();

        var result = await response.Content.ReadFromJsonAsync<DocuSealTemplateResponse>(JsonOptions, ct);
        return result?.Id ?? throw new InvalidOperationException("DocuSeal returned no template ID");
    }

    private async Task<int> CreateBlankTemplateAsync(string name, CancellationToken ct)
    {
        logger.LogInformation("DocuSeal CreateTemplate (HTML blank): {Name}", name);

        var html = $"<p style=\"font-family:sans-serif;padding:40px\">" +
                   $"<strong>{System.Net.WebUtility.HtmlEncode(name)}</strong><br/><br/>" +
                   $"By signing below, you acknowledge receipt and review of this form.</p>";

        var body = JsonContent.Create(new { name, html }, options: JsonOptions);
        using var req = await BuildRequestAsync(HttpMethod.Post, "/api/templates/html", ct);
        req.Content = body;
        using var response = await httpClient.SendAsync(req, ct);
        response.EnsureSuccessStatusCode();

        var result = await response.Content.ReadFromJsonAsync<DocuSealTemplateResponse>(JsonOptions, ct);
        return result?.Id ?? throw new InvalidOperationException("DocuSeal returned no template ID from HTML template");
    }

    public async Task<DocumentSigningSubmission> CreateSubmissionAsync(int templateId, string signerEmail, string signerName, CancellationToken ct)
    {
        logger.LogInformation("DocuSeal CreateSubmission: template {TemplateId} for {Email}", templateId, signerEmail);

        var request = new DocuSealSubmissionRequest
        {
            TemplateId = templateId,
            SendEmail = false,
            Submitters =
            [
                new DocuSealSubmitter
                {
                    Email = signerEmail,
                    Name = signerName,
                    Role = "First Party",
                },
            ],
        };

        var (_, publicBaseUrl) = await ReadUrlsAsync(ct);

        using var req = await BuildRequestAsync(HttpMethod.Post, "/api/submissions", ct);
        req.Content = JsonContent.Create(request, options: JsonOptions);
        using var response = await httpClient.SendAsync(req, ct);
        response.EnsureSuccessStatusCode();

        var results = await response.Content.ReadFromJsonAsync<List<DocuSealSubmissionResponse>>(JsonOptions, ct);
        var submission = results?.FirstOrDefault()
            ?? throw new InvalidOperationException("DocuSeal returned no submission");

        return new DocumentSigningSubmission(submission.Id, RewriteEmbedUrl(submission.EmbedSrc, publicBaseUrl));
    }

    public async Task<DocumentSigningMultiSubmission> CreateSubmissionFromPdfAsync(
        string templateName,
        byte[] pdfBytes,
        IReadOnlyList<SequentialSubmitter> submitters,
        CancellationToken ct)
    {
        logger.LogInformation(
            "DocuSeal CreateSubmissionFromPdf: '{Name}' ({Size} bytes), {Count} submitters",
            templateName, pdfBytes.Length, submitters.Count);

        var templateId = await CreateTemplateFromPdfAsync(templateName, pdfBytes, ct);

        var submitterDtos = submitters
            .OrderBy(s => s.Order)
            .Select(s => new DocuSealSubmitter
            {
                Email = s.Email,
                Name = s.Name,
                Role = s.Role,
            })
            .ToList();

        var request = new DocuSealSubmissionRequest
        {
            TemplateId = templateId,
            SendEmail = false,
            Submitters = submitterDtos,
        };

        var (_, publicBaseUrl) = await ReadUrlsAsync(ct);

        using var req = await BuildRequestAsync(HttpMethod.Post, "/api/submissions", ct);
        req.Content = JsonContent.Create(request, options: JsonOptions);
        using var response = await httpClient.SendAsync(req, ct);
        response.EnsureSuccessStatusCode();

        var results = await response.Content.ReadFromJsonAsync<List<DocuSealSubmissionResponse>>(JsonOptions, ct)
            ?? throw new InvalidOperationException("DocuSeal returned no submission results");

        var orderedSubmitters = submitters.OrderBy(s => s.Order).ToList();
        var byOrder = new Dictionary<int, SubmitterResult>();

        for (var i = 0; i < Math.Min(results.Count, orderedSubmitters.Count); i++)
        {
            var order = orderedSubmitters[i].Order;
            byOrder[order] = new SubmitterResult(results[i].Id, RewriteEmbedUrl(results[i].EmbedSrc, publicBaseUrl));
        }

        return new DocumentSigningMultiSubmission(templateId, byOrder);
    }

    public async Task<byte[]> GetSignedPdfAsync(int submissionId, CancellationToken ct)
    {
        logger.LogInformation("DocuSeal GetSignedPdf: submission {Id}", submissionId);

        using var req = await BuildRequestAsync(HttpMethod.Get, $"/api/submissions/{submissionId}", ct);
        using var response = await httpClient.SendAsync(req, ct);
        response.EnsureSuccessStatusCode();

        var submission = await response.Content.ReadFromJsonAsync<DocuSealSubmissionDetailResponse>(JsonOptions, ct);
        var documentUrl = submission?.Documents?.FirstOrDefault()?.Url;

        if (string.IsNullOrEmpty(documentUrl))
            throw new InvalidOperationException($"No signed document found for submission {submissionId}");

        // Document URL is absolute — fetch via the same authenticated client.
        using var docReq = new HttpRequestMessage(HttpMethod.Get, documentUrl);
        await AttachAuthAsync(docReq, ct);
        using var docResponse = await httpClient.SendAsync(docReq, ct);
        docResponse.EnsureSuccessStatusCode();
        return await docResponse.Content.ReadAsByteArrayAsync(ct);
    }

    public async Task<DocumentSigningSubmissionStatus> GetSubmissionStatusAsync(int submissionId, CancellationToken ct)
    {
        logger.LogInformation("DocuSeal GetSubmissionStatus: submission {Id}", submissionId);

        using var req = await BuildRequestAsync(HttpMethod.Get, $"/api/submissions/{submissionId}", ct);
        using var response = await httpClient.SendAsync(req, ct);
        response.EnsureSuccessStatusCode();

        var submission = await response.Content.ReadFromJsonAsync<DocuSealSubmissionDetailResponse>(JsonOptions, ct);
        return new DocumentSigningSubmissionStatus(
            submission?.Status ?? "unknown",
            submission?.CompletedAt);
    }

    public async Task DeleteTemplateAsync(int templateId, CancellationToken ct)
    {
        logger.LogInformation("DocuSeal DeleteTemplate: {Id}", templateId);

        using var req = await BuildRequestAsync(HttpMethod.Delete, $"/api/templates/{templateId}", ct);
        using var response = await httpClient.SendAsync(req, ct);
        response.EnsureSuccessStatusCode();
    }

    /// <summary>
    /// Build a request with absolute URL + X-Auth-Token header from
    /// the live DocuSeal settings.
    /// </summary>
    private async Task<HttpRequestMessage> BuildRequestAsync(HttpMethod method, string path, CancellationToken ct)
    {
        var (baseUrl, _) = await ReadUrlsAsync(ct);
        var url = baseUrl.TrimEnd('/') + path;
        var req = new HttpRequestMessage(method, url);
        await AttachAuthAsync(req, ct);
        // Per-request timeout — HttpClient.Timeout is shared across the
        // singleton, so set on the per-request CancellationToken instead
        // when we wire deeper. For now we rely on HttpClient default
        // (100s) which is well under DocuSeal's typical operation time.
        return req;
    }

    private async Task AttachAuthAsync(HttpRequestMessage req, CancellationToken ct)
    {
        var apiKey = await settings.GetStringAsync(DocuSealSettings.KeyApiKey, ct);
        if (!string.IsNullOrEmpty(apiKey))
        {
            req.Headers.TryAddWithoutValidation("X-Auth-Token", apiKey);
        }
    }

    private async Task<(string BaseUrl, string PublicBaseUrl)> ReadUrlsAsync(CancellationToken ct)
    {
        var baseUrl = await settings.GetStringAsync(DocuSealSettings.KeyApiUrl, ct)
            ?? "http://qb-engineer-signing:3000";
        var publicBaseUrl = await settings.GetStringAsync(DocuSealSettings.KeyPublicBaseUrl, ct)
            ?? string.Empty;
        return (baseUrl, publicBaseUrl);
    }

    /// <summary>
    /// Rewrites a DocuSeal embed URL from the internal Docker network
    /// address to the browser-accessible proxy URL, when configured.
    /// </summary>
    private static string RewriteEmbedUrl(string embedSrc, string publicBaseUrl)
    {
        if (string.IsNullOrEmpty(embedSrc) || string.IsNullOrEmpty(publicBaseUrl))
            return embedSrc;

        // The embed URL points back at the DocuSeal instance. To rewrite,
        // we strip the existing scheme+host and prepend publicBaseUrl.
        try
        {
            var uri = new Uri(embedSrc);
            return publicBaseUrl.TrimEnd('/') + uri.PathAndQuery;
        }
        catch (UriFormatException)
        {
            return embedSrc;
        }
    }

    // ─── DocuSeal API DTOs ───

    private sealed class DocuSealTemplateResponse
    {
        public int Id { get; set; }
        public string Name { get; set; } = string.Empty;
    }

    private sealed class DocuSealSubmissionRequest
    {
        public int TemplateId { get; set; }
        public bool SendEmail { get; set; }
        public List<DocuSealSubmitter> Submitters { get; set; } = [];
    }

    private sealed class DocuSealSubmitter
    {
        public string Email { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public string Role { get; set; } = string.Empty;
    }

    private sealed class DocuSealSubmissionResponse
    {
        public int Id { get; set; }
        public string EmbedSrc { get; set; } = string.Empty;
    }

    private sealed class DocuSealSubmissionDetailResponse
    {
        public int Id { get; set; }
        public string Status { get; set; } = string.Empty;
        public DateTimeOffset? CompletedAt { get; set; }
        public List<DocuSealDocument>? Documents { get; set; }
    }

    private sealed class DocuSealDocument
    {
        public string Url { get; set; } = string.Empty;
        public string Filename { get; set; } = string.Empty;
    }
}
