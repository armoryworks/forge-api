using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

using PuppeteerSharp;

using Forge.Core.Interfaces;
using Forge.Core.Models;

namespace Forge.Integrations;

/// <summary>
/// Generates driver.js walkthrough tour steps by:
/// 1. Navigating to the live Angular app (authenticated via injected JWT) using PuppeteerSharp.
/// 2. Extracting the live DOM — buttons, headings, tabs, filter labels, Angular component selectors.
/// 3. Sending the structured DOM description to Ollama and parsing the returned JSON step array.
///
/// Owns its own singleton browser instance (separate from PdfJsExtractorService and
/// PuppeteerFormRendererService) to avoid cross-service locking contention.
/// </summary>
public class PuppeteerWalkthroughGeneratorService : IWalkthroughGeneratorService, IAsyncDisposable
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<PuppeteerWalkthroughGeneratorService> _logger;
    private readonly string _frontendBaseUrl;
    private readonly string _localStorageTokenKey;
    private readonly SemaphoreSlim _browserLock = new(1, 1);
    private IBrowser? _browser;
    private bool _disposed;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    private static readonly HashSet<string> ValidSides =
        new(StringComparer.OrdinalIgnoreCase) { "top", "bottom", "left", "right" };

    private static readonly HashSet<string> ValidAligns =
        new(StringComparer.OrdinalIgnoreCase) { "start", "center", "end" };

    public PuppeteerWalkthroughGeneratorService(
        IServiceScopeFactory scopeFactory,
        ILogger<PuppeteerWalkthroughGeneratorService> logger,
        IConfiguration configuration)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
        _frontendBaseUrl = configuration["FormRendererBaseUrl"]
                           ?? configuration["FrontendBaseUrl"]
                           ?? "http://localhost:4200";
        // Match the key the Angular app uses to store its JWT
        _localStorageTokenKey = configuration["Auth:LocalStorageTokenKey"] ?? "qbe-token";
    }

    public async Task<List<WalkthroughStep>> GenerateStepsAsync(
        string appRoute,
        int moduleId,
        string jwtToken,
        CancellationToken ct)
    {
        _logger.LogInformation(
            "Generating walkthrough steps for module {ModuleId} at route {Route}", moduleId, appRoute);

        var browser = await GetBrowserAsync(ct);
        await using var page = await browser.NewPageAsync();

        try
        {
            await page.SetViewportAsync(new ViewPortOptions { Width = 1440, Height = 900 });

            // Inject JWT into localStorage before Angular bootstraps so auth guards grant access
            await page.EvaluateFunctionOnNewDocumentAsync(
                @"(key, token) => { localStorage.setItem(key, token); }",
                _localStorageTokenKey, jwtToken);

            var url = $"{_frontendBaseUrl.TrimEnd('/')}{appRoute}";
            _logger.LogDebug("Navigating to {Url}", url);

            await page.GoToAsync(url, new NavigationOptions
            {
                WaitUntil = [WaitUntilNavigation.Networkidle2],
                Timeout = 45_000,
            });

            // Wait for Angular to fully render
            await page.WaitForSelectorAsync("app-root", new WaitForSelectorOptions { Timeout = 20_000 });
            await Task.Delay(3_000, ct);

            // Extract DOM signals
            var domJson = await page.EvaluateFunctionAsync<string>(DomExtractionScript);
            _logger.LogDebug("DOM extraction result: {Dom}", domJson?.Length > 200
                ? domJson[..200] + "…"
                : domJson);

            // Build prompt and call Ollama
            var prompt = BuildPrompt(appRoute, domJson ?? "{}");
            string rawResponse;
            using (var scope = _scopeFactory.CreateScope())
            {
                var aiService = scope.ServiceProvider.GetRequiredService<IAiService>();
                rawResponse = await aiService.GenerateTextAsync(prompt, SystemPrompt, 0.1, ct);
            }

            _logger.LogDebug("Ollama raw response ({Len} chars): {Preview}",
                rawResponse.Length, rawResponse.Length > 300 ? rawResponse[..300] + "…" : rawResponse);

            var steps = ParseSteps(rawResponse);
            _logger.LogInformation(
                "Generated {Count} walkthrough steps for module {ModuleId}", steps.Count, moduleId);

            return steps;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogError(ex, "Walkthrough generation failed for module {ModuleId}", moduleId);
            throw;
        }
    }

    // ─── DOM extraction ──────────────────────────────────────────────────────

    /// <summary>
    /// JavaScript evaluated inside the Chromium page. Returns a JSON string of page signals
    /// that Ollama will use to write tour steps.
    /// </summary>
    private const string DomExtractionScript = @"() => {
        const getVisible = (sel) => Array.from(document.querySelectorAll(sel)).filter(el => {
            const r = el.getBoundingClientRect();
            return r.width > 0 && r.height > 0;
        });

        const getText = (el) => {
            const label = el.getAttribute('aria-label') || el.getAttribute('placeholder');
            if (label) return label.trim();
            const text = (el.innerText || el.textContent || '').trim().replace(/\s+/g, ' ');
            return text.length > 100 ? text.substring(0, 100) : text;
        };

        const headings = getVisible('h1, h2, .page-header__title, app-page-header .mat-h1, [class*=""page-header""] h1, [class*=""page-header""] h2')
            .map(el => ({ tag: el.tagName.toLowerCase(), text: getText(el) }))
            .filter(h => h.text.length > 0).slice(0, 6);

        const buttons = getVisible('button:not(.driver-popover button), [role=""button""]')
            .map(el => ({
                text: getText(el),
                classes: el.className ? el.className.toString().substring(0, 120) : '',
                ariaLabel: el.getAttribute('aria-label') || null,
            }))
            .filter(b => b.text.length > 0 && b.text !== '×')
            .slice(0, 20);

        const tabs = getVisible('.tab, .tab-bar .tab, [role=""tab""], .mat-mdc-tab')
            .map(el => getText(el))
            .filter(t => t.length > 0)
            .slice(0, 10);

        const filterLabels = getVisible('mat-label, .mat-mdc-floating-label, .filters-bar app-input, .filters-bar app-select')
            .map(el => getText(el))
            .filter(l => l.length > 0 && l !== '*')
            .slice(0, 10);

        const appComponents = [...new Set(
            getVisible('[class^=""app-""], [_nghost]')
                .map(el => el.tagName.toLowerCase())
                .filter(t => t.startsWith('app-') && t !== 'app-root')
        )].slice(0, 12);

        const pageTitle = document.querySelector('h1, .page-header__title, app-page-header')?.innerText?.trim()
            || document.title?.replace(' - QB Engineer', '').trim()
            || 'this page';

        return JSON.stringify({ pageTitle, headings, buttons, tabs, filterLabels, appComponents, url: window.location.pathname });
    }";

    // ─── AI prompt ───────────────────────────────────────────────────────────

    private const string SystemPrompt = """
        You are a UX documentation assistant that generates driver.js interactive tour steps for a manufacturing ERP web application called QB Engineer.
        You MUST respond with ONLY a valid JSON array. No prose, no markdown code fences, no explanation — just the raw JSON array.
        Each element in the array must have exactly this shape:
        {"element":"CSS selector string or null","popover":{"title":"short title","description":"1-3 sentence explanation","side":"bottom","align":"start"}}
        Rules:
        - "element" must be a valid CSS selector that exists on the page (from the provided DOM signals), or null for intro/outro steps.
        - Prefer these reliable selectors: app-page-header, .action-btn--primary, .filters-bar, app-data-table, app-detail-side-panel, .tab-bar, specific aria-labels like [aria-label="X"].
        - Never invent selectors not present in the DOM signals.
        - Generate 5–8 steps total.
        - First step: element=null, page overview (what this screen does and why it matters).
        - Middle steps: highlight the most important interactive elements in logical order.
        - Last step: element=null, "You're all set!" encouragement.
        - side must be one of: top, bottom, left, right. align must be one of: start, center, end.
        - Keep descriptions concise, friendly, and specific to manufacturing operations context.
        """;

    private static string BuildPrompt(string appRoute, string domJson)
    {
        JsonElement dom;
        try { dom = JsonSerializer.Deserialize<JsonElement>(domJson); }
        catch { dom = default; }

        var sb = new StringBuilder();
        sb.AppendLine($"Generate a driver.js walkthrough tour for the QB Engineer page at route: {appRoute}");
        sb.AppendLine();

        if (dom.ValueKind == JsonValueKind.Object)
        {
            if (dom.TryGetProperty("pageTitle", out var title))
                sb.AppendLine($"Page title: {title.GetString()}");

            if (dom.TryGetProperty("headings", out var headings) && headings.ValueKind == JsonValueKind.Array)
            {
                sb.AppendLine("Headings on the page:");
                foreach (var h in headings.EnumerateArray())
                    if (h.TryGetProperty("text", out var t)) sb.AppendLine($"  - {t.GetString()}");
            }

            if (dom.TryGetProperty("tabs", out var tabs) && tabs.ValueKind == JsonValueKind.Array)
            {
                var tabList = tabs.EnumerateArray().Select(t => t.GetString()).Where(t => t?.Length > 0).ToList();
                if (tabList.Count > 0)
                    sb.AppendLine($"Tab labels: {string.Join(", ", tabList)}");
            }

            if (dom.TryGetProperty("buttons", out var buttons) && buttons.ValueKind == JsonValueKind.Array)
            {
                sb.AppendLine("Buttons visible on the page:");
                foreach (var b in buttons.EnumerateArray())
                {
                    var text = b.TryGetProperty("text", out var t) ? t.GetString() : "";
                    var cls = b.TryGetProperty("classes", out var c) ? c.GetString() : "";
                    var label = b.TryGetProperty("ariaLabel", out var a) ? a.GetString() : null;
                    sb.AppendLine($"  - \"{text}\" classes=\"{cls}\" aria-label=\"{label}\"");
                }
            }

            if (dom.TryGetProperty("filterLabels", out var filters) && filters.ValueKind == JsonValueKind.Array)
            {
                var filterList = filters.EnumerateArray().Select(f => f.GetString()).Where(f => f?.Length > 0).ToList();
                if (filterList.Count > 0)
                    sb.AppendLine($"Filter/input fields: {string.Join(", ", filterList)}");
            }

            if (dom.TryGetProperty("appComponents", out var comps) && comps.ValueKind == JsonValueKind.Array)
            {
                var compList = comps.EnumerateArray().Select(c => c.GetString()).Where(c => c?.Length > 0).ToList();
                if (compList.Count > 0)
                    sb.AppendLine($"Angular components present: {string.Join(", ", compList)}");
            }
        }

        sb.AppendLine();
        sb.AppendLine("Return ONLY the JSON array of steps. No other text.");
        return sb.ToString();
    }

    // ─── Response parsing ────────────────────────────────────────────────────

    private static List<WalkthroughStep> ParseSteps(string raw)
    {
        var json = raw.Trim();

        // Strip markdown code fences if present (Ollama sometimes wraps output)
        if (json.StartsWith("```", StringComparison.Ordinal))
        {
            var start = json.IndexOf('[');
            var end = json.LastIndexOf(']');
            if (start >= 0 && end > start)
                json = json[start..(end + 1)];
        }

        // Find the JSON array bounds if there's surrounding prose
        if (!json.StartsWith('['))
        {
            var start = json.IndexOf('[');
            var end = json.LastIndexOf(']');
            if (start >= 0 && end > start)
                json = json[start..(end + 1)];
        }

        List<RawStep>? raw2;
        try
        {
            raw2 = JsonSerializer.Deserialize<List<RawStep>>(json, JsonOptions);
        }
        catch (JsonException)
        {
            // Last-resort fallback — return a single intro step
            return
            [
                new WalkthroughStep
                {
                    Element = null,
                    Popover = new WalkthroughPopover
                    {
                        Title = "Tour",
                        Description = "Explore this page to learn its features.",
                        Side = "bottom",
                        Align = "center",
                    },
                },
            ];
        }

        return (raw2 ?? []).Select(Sanitize).ToList();
    }

    private static WalkthroughStep Sanitize(RawStep s) => new()
    {
        Element = string.IsNullOrWhiteSpace(s.Element) ? null : s.Element.Trim(),
        Popover = new WalkthroughPopover
        {
            Title = (s.Popover?.Title ?? "Step").Trim(),
            Description = (s.Popover?.Description ?? "").Trim(),
            Side = ValidSides.Contains(s.Popover?.Side ?? "") ? s.Popover!.Side! : "bottom",
            Align = ValidAligns.Contains(s.Popover?.Align ?? "") ? s.Popover!.Align! : "start",
        },
    };

    // Intermediate deserialization types (camelCase from Ollama)
    private sealed class RawStep
    {
        public string? Element { get; set; }
        public RawPopover? Popover { get; set; }
    }

    private sealed class RawPopover
    {
        public string? Title { get; set; }
        public string? Description { get; set; }
        public string? Side { get; set; }
        public string? Align { get; set; }
    }

    // ─── Browser lifecycle ───────────────────────────────────────────────────

    private async Task<IBrowser> GetBrowserAsync(CancellationToken ct)
    {
        if (_browser is { IsClosed: false })
            return _browser;

        await _browserLock.WaitAsync(ct);
        try
        {
            if (_browser is { IsClosed: false })
                return _browser;

            _logger.LogInformation("Launching headless Chromium for walkthrough generation");

            var executablePath = Environment.GetEnvironmentVariable("PUPPETEER_EXECUTABLE_PATH");
            if (string.IsNullOrEmpty(executablePath))
            {
                var browserFetcher = new BrowserFetcher();
                await browserFetcher.DownloadAsync();
            }

            _browser = await Puppeteer.LaunchAsync(new LaunchOptions
            {
                Headless = true,
                Args =
                [
                    "--no-sandbox",
                    "--disable-setuid-sandbox",
                    "--disable-dev-shm-usage",
                    "--disable-gpu",
                ],
                ExecutablePath = executablePath,
            });

            _logger.LogInformation("Headless Chromium launched for walkthrough generator (PID: {Pid})",
                _browser.Process?.Id);

            return _browser;
        }
        finally
        {
            _browserLock.Release();
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed) return;
        _disposed = true;

        if (_browser is not null)
        {
            try
            {
                await _browser.CloseAsync();
                _browser.Dispose();
            }
            catch
            {
                // Best-effort cleanup
            }
        }

        _browserLock.Dispose();
        GC.SuppressFinalize(this);
    }
}
