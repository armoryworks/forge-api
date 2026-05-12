using System.Text.Json;

using MediatR;

using Microsoft.EntityFrameworkCore;

using Forge.Core.Entities;
using Forge.Core.Interfaces;
using Forge.Data.Context;

namespace Forge.Api.Features.ComplianceForms;

public record StateFormDefinitionResult(
    string StateCode,
    string StateName,
    string Category,
    int? FormDefinitionVersionId,
    string? FormDefinitionJson);

public record GetMyStateFormDefinitionQuery(int UserId) : IRequest<StateFormDefinitionResult>;

public class GetMyStateFormDefinitionHandler(
    AppDbContext db,
    IPdfJsExtractorService pdfJsExtractor,
    IFormDefinitionParser formParser,
    IFormDefinitionVerifier formVerifier,
    IFormDefinitionBuilderFactory builderFactory,
    IHttpClientFactory httpClientFactory,
    ILogger<GetMyStateFormDefinitionHandler> logger) : IRequestHandler<GetMyStateFormDefinitionQuery, StateFormDefinitionResult>
{
    public async Task<StateFormDefinitionResult> Handle(GetMyStateFormDefinitionQuery request, CancellationToken ct)
    {
        // 1. Resolve user's state (same 3-tier logic as GetProfileCompleteness)
        var user = await db.Users
            .AsNoTracking()
            .Include(u => u.WorkLocation)
            .FirstOrDefaultAsync(u => u.Id == request.UserId, ct);

        var stateCode = user?.WorkLocation?.State;

        if (string.IsNullOrWhiteSpace(stateCode))
        {
            var defaultLocation = await db.CompanyLocations
                .AsNoTracking()
                .FirstOrDefaultAsync(l => l.IsDefault && l.IsActive, ct);
            stateCode = defaultLocation?.State;
        }

        if (string.IsNullOrWhiteSpace(stateCode))
        {
            var companySetting = await db.SystemSettings
                .AsNoTracking()
                .FirstOrDefaultAsync(s => s.Key == "company_state", ct);
            stateCode = companySetting?.Value;
        }

        if (string.IsNullOrWhiteSpace(stateCode))
            return new StateFormDefinitionResult("", "Unknown", "no_state", null, null);

        // 2. Look up state reference data
        var stateRef = await db.ReferenceData
            .AsNoTracking()
            .FirstOrDefaultAsync(r => r.GroupCode == "state_withholding" && r.Code == stateCode, ct);

        if (stateRef is null)
            return new StateFormDefinitionResult(stateCode, stateCode, "unknown", null, null);

        var category = "state_form";
        string? sourceUrl = null;

        if (!string.IsNullOrWhiteSpace(stateRef.Metadata))
        {
            try
            {
                using var doc = JsonDocument.Parse(stateRef.Metadata);
                if (doc.RootElement.TryGetProperty("category", out var cat))
                    category = cat.GetString() ?? "state_form";
                if (doc.RootElement.TryGetProperty("sourceUrl", out var url))
                    sourceUrl = url.GetString();
            }
            catch (JsonException) { }
        }

        // 3. No-tax or federal-only states don't need a form definition
        if (category is "no_tax" or "federal")
            return new StateFormDefinitionResult(stateCode, stateRef.Label, category, null, null);

        // 4. Check for current effective version for this state
        var now = DateTimeOffset.UtcNow;
        var currentVersion = await db.FormDefinitionVersions
            .AsNoTracking()
            .Where(v => v.StateCode == stateCode && v.IsActive
                        && v.EffectiveDate <= now
                        && (v.ExpirationDate == null || v.ExpirationDate > now))
            .OrderByDescending(v => v.EffectiveDate)
            .FirstOrDefaultAsync(ct);

        if (currentVersion is not null)
        {
            // Invalidate if hardcoded builder has been updated since this version was stored
            var stateBuilderForCheck = builderFactory.TryGetStateBuilder(stateCode);
            var builderVersionStale = stateBuilderForCheck is not null
                && !StoredJsonHasBuilderVersion(currentVersion.FormDefinitionJson, stateBuilderForCheck.BuilderVersion);

            if (!builderVersionStale)
                return new StateFormDefinitionResult(stateCode, stateRef.Label, category,
                    currentVersion.Id, currentVersion.FormDefinitionJson);

            // Mark stale version as inactive so a fresh one is generated below
            logger.LogInformation("Builder version changed for state {StateCode} — invalidating cached form definition", stateCode);
            currentVersion.IsActive = false;
            await db.SaveChangesAsync(ct);
        }

        // 5. Resolve source URL — prefer StateWithholdingUrls (maintained in code)
        {
            var urls = Data.StateWithholdingUrls.GetAll();
            if (urls.TryGetValue(stateCode, out var codeUrl))
                sourceUrl = codeUrl;
        }

        if (string.IsNullOrWhiteSpace(sourceUrl))
        {
            logger.LogWarning("No source URL for state withholding form: {StateCode}", stateCode);
            return new StateFormDefinitionResult(stateCode, stateRef.Label, category, null, null);
        }

        // 6. Download PDF and extract form definition via pdf.js → create new version
        try
        {
            logger.LogInformation("Extracting state withholding form definition for {StateCode} from {Url}", stateCode, sourceUrl);

            using var httpClient = httpClientFactory.CreateClient();
            httpClient.DefaultRequestHeaders.UserAgent.ParseAdd(
                "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/131.0.0.0 Safari/537.36");
            var pdfBytes = await httpClient.GetByteArrayAsync(sourceUrl, ct);

            var formType = $"StateWithholding_{stateCode}";
            var rawResult = await pdfJsExtractor.ExtractRawAsync(pdfBytes, ct);

            // Prefer hardcoded state builder (pixel-perfect), fall back to generic parser
            var stateBuilder = builderFactory.TryGetStateBuilder(stateCode);
            string formDefJson;
            if (stateBuilder is not null)
            {
                logger.LogInformation("Using hardcoded builder for state {StateCode}", stateCode);
                formDefJson = stateBuilder.Build(rawResult);
            }
            else
            {
                formDefJson = formParser.Parse(rawResult, formType);

                // Verify and refine with AI (only for generic parser output)
                var verification = await formVerifier.VerifyAsync(formDefJson, rawResult, formType, ct);
                if (verification.CorrectedJson is not null)
                    formDefJson = verification.CorrectedJson;
            }

            if (!string.IsNullOrWhiteSpace(formDefJson))
            {
                formDefJson = ImproveExtractedTitle(formDefJson, stateRef, stateCode);

                var fieldCount = System.Text.RegularExpressions.Regex.Matches(formDefJson, @"""id""").Count;
                var version = new FormDefinitionVersion
                {
                    StateCode = stateCode,
                    FormDefinitionJson = formDefJson,
                    SourceUrl = sourceUrl,
                    EffectiveDate = now,
                    Revision = now.ToString("yyyy-MM"),
                    ExtractedAt = now,
                    FieldCount = fieldCount,
                    IsActive = true,
                };
                db.FormDefinitionVersions.Add(version);
                await db.SaveChangesAsync(ct);

                logger.LogInformation("Created form definition version {VersionId} for state {StateCode}", version.Id, stateCode);
                return new StateFormDefinitionResult(stateCode, stateRef.Label, category,
                    version.Id, formDefJson);
            }
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to extract state withholding form definition for {StateCode}", stateCode);
        }

        return new StateFormDefinitionResult(stateCode, stateRef.Label, category, null, null);
    }

    /// <summary>
    /// Replace the auto-extracted title (often a form number) with a human-friendly title
    /// using state reference data metadata.
    /// </summary>
    private static string ImproveExtractedTitle(string formDefJson, Forge.Core.Entities.ReferenceData stateRef, string stateCode)
    {
        try
        {
            using var doc = JsonDocument.Parse(formDefJson);
            var root = doc.RootElement;

            string? formName = null;
            if (!string.IsNullOrWhiteSpace(stateRef.Metadata))
            {
                using var metaDoc = JsonDocument.Parse(stateRef.Metadata);
                if (metaDoc.RootElement.TryGetProperty("formName", out var fn))
                    formName = fn.GetString();
            }

            var title = formName is not null
                ? $"{stateRef.Label} {formName} — Employee's Withholding Certificate"
                : $"{stateRef.Label} State Withholding Certificate";

            var dict = JsonSerializer.Deserialize<Dictionary<string, object>>(formDefJson)!;
            dict["title"] = title;
            dict["formNumber"] = formName ?? $"StateWithholding_{stateCode}";
            return JsonSerializer.Serialize(dict, new JsonSerializerOptions
            {
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
                WriteIndented = false,
            });
        }
        catch
        {
            return formDefJson;
        }
    }

    private static bool StoredJsonHasBuilderVersion(string json, string expectedVersion)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            if (doc.RootElement.TryGetProperty("builderVersion", out var bv))
                return bv.GetString() == expectedVersion;
        }
        catch { }
        return false;
    }
}
