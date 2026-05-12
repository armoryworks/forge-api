using System.Text.Json;

using Microsoft.Extensions.Logging;

using PuppeteerSharp;

using Forge.Core.Interfaces;
using Forge.Core.Models;

namespace Forge.Integrations;

/// <summary>
/// Extracts raw text and form field data from PDFs using pdf.js via PuppeteerSharp (headless Chromium).
/// Manages a singleton browser instance that is lazily initialized and reused across extractions.
/// </summary>
public class PdfJsExtractorService : IPdfJsExtractorService, IAsyncDisposable
{
    private readonly ILogger<PdfJsExtractorService> _logger;
    private readonly string _extractorPagePath;
    private readonly SemaphoreSlim _browserLock = new(1, 1);
    private IBrowser? _browser;
    private bool _disposed;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
    };

    public PdfJsExtractorService(ILogger<PdfJsExtractorService> logger)
    {
        _logger = logger;

        // Resolve the bundled extraction page path.
        // In published builds, AppContext.BaseDirectory contains wwwroot/.
        // In dev (dotnet watch), wwwroot is in the project directory, not bin/.
        _extractorPagePath = ResolvePath("pdf-extract.html");

        if (!File.Exists(_extractorPagePath))
            _logger.LogWarning("PDF extractor page not found at {Path} — extraction will fail", _extractorPagePath);
        else
            _logger.LogInformation("PDF extractor page resolved at {Path}", _extractorPagePath);
    }

    private static string ResolvePath(string fileName)
    {
        // Try AppContext.BaseDirectory first (published builds)
        var candidate = Path.Combine(AppContext.BaseDirectory, "wwwroot", fileName);
        if (File.Exists(candidate)) return candidate;

        // Walk up from bin/Debug/net9.0 toward the project root (dev / dotnet watch)
        var dir = AppContext.BaseDirectory;
        for (var i = 0; i < 4; i++)
        {
            dir = Path.GetDirectoryName(dir) ?? dir;
            candidate = Path.Combine(dir, "wwwroot", fileName);
            if (File.Exists(candidate)) return candidate;
        }

        // Fallback: return the original path (will fail at extraction time with a clear message)
        return Path.Combine(AppContext.BaseDirectory, "wwwroot", fileName);
    }

    public async Task<PdfExtractionResult> ExtractRawAsync(byte[] pdfBytes, CancellationToken ct)
    {
        _logger.LogInformation("Extracting PDF structure via pdf.js ({Size} bytes)", pdfBytes.Length);

        var browser = await GetBrowserAsync(ct);
        await using var page = await browser.NewPageAsync();

        try
        {
            // Navigate to the bundled extraction page
            var fileUrl = $"file://{_extractorPagePath.Replace('\\', '/')}";
            await page.GoToAsync(fileUrl, new NavigationOptions
            {
                WaitUntil = [WaitUntilNavigation.DOMContentLoaded],
                Timeout = 30_000,
            });

            // Wait for pdf.js to be ready
            await page.WaitForExpressionAsync("window.__pdfExtractorReady === true",
                new WaitForFunctionOptions { Timeout = 30_000 });

            // Call the extraction function with PDF bytes as base64
            var base64 = Convert.ToBase64String(pdfBytes);
            var result = await page.EvaluateFunctionAsync<JsonElement>(
                "async (b64) => await window.extractFormStructure(b64)",
                base64);

            var extraction = JsonSerializer.Deserialize<PdfExtractionResult>(
                result.GetRawText(), JsonOptions);

            if (extraction is null)
                throw new InvalidOperationException("pdf.js extraction returned null");

            _logger.LogInformation(
                "pdf.js extraction complete: {PageCount} pages, {FieldCount} annotations, {TextCount} text items",
                extraction.PageCount,
                extraction.Pages.Sum(p => p.Annotations.Count),
                extraction.Pages.Sum(p => p.TextItems.Count));

            return extraction;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogError(ex, "pdf.js extraction failed");
            throw;
        }
    }

    public async Task<byte[]> RenderPageAsImageAsync(byte[] pdfBytes, int pageNumber, double scale, CancellationToken ct)
    {
        _logger.LogInformation("Rendering PDF page {Page} as image (scale={Scale}, {Size} bytes)",
            pageNumber, scale, pdfBytes.Length);

        var browser = await GetBrowserAsync(ct);
        await using var page = await browser.NewPageAsync();

        try
        {
            var fileUrl = $"file://{_extractorPagePath.Replace('\\', '/')}";
            await page.GoToAsync(fileUrl, new NavigationOptions
            {
                WaitUntil = [WaitUntilNavigation.DOMContentLoaded],
                Timeout = 30_000,
            });

            await page.WaitForExpressionAsync("window.__pdfExtractorReady === true",
                new WaitForFunctionOptions { Timeout = 30_000 });

            var base64 = Convert.ToBase64String(pdfBytes);
            var pngBase64 = await page.EvaluateFunctionAsync<string>(
                "async (b64, pageNum, s) => await window.renderPdfPageToImage(b64, pageNum, s)",
                base64, pageNumber, scale);

            if (string.IsNullOrEmpty(pngBase64))
                throw new InvalidOperationException($"pdf.js page render returned empty for page {pageNumber}");

            var imageBytes = Convert.FromBase64String(pngBase64);
            _logger.LogInformation("Rendered PDF page {Page} as PNG ({ImageSize} bytes)", pageNumber, imageBytes.Length);
            return imageBytes;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogError(ex, "pdf.js page render failed for page {Page}", pageNumber);
            throw;
        }
    }

    private async Task<IBrowser> GetBrowserAsync(CancellationToken ct)
    {
        if (_browser is { IsClosed: false })
            return _browser;

        await _browserLock.WaitAsync(ct);
        try
        {
            if (_browser is { IsClosed: false })
                return _browser;

            _logger.LogInformation("Launching headless Chromium for pdf.js extraction");

            // Use system-installed Chromium if PUPPETEER_EXECUTABLE_PATH is set,
            // otherwise download Chromium via PuppeteerSharp
            var executablePath = Environment.GetEnvironmentVariable("PUPPETEER_EXECUTABLE_PATH");
            if (string.IsNullOrEmpty(executablePath))
            {
                _logger.LogInformation("Downloading Chromium via PuppeteerSharp...");
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
                    "--single-process",
                ],
                ExecutablePath = executablePath,
            });

            _logger.LogInformation("Headless Chromium launched (PID: {Pid})",
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
