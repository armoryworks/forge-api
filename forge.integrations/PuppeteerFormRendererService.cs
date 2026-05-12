using System.Text.Json;

using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

using PuppeteerSharp;

using Forge.Core.Interfaces;

namespace Forge.Integrations;

/// <summary>
/// Renders ComplianceFormDefinition JSON to PNG screenshots via PuppeteerSharp.
/// Navigates to the Angular headless render route (/__render-form), injects the definition,
/// and screenshots each page.
/// </summary>
public class PuppeteerFormRendererService : IFormRendererService, IAsyncDisposable
{
    private readonly ILogger<PuppeteerFormRendererService> _logger;
    private readonly string _frontendBaseUrl;
    private readonly SemaphoreSlim _browserLock = new(1, 1);
    private IBrowser? _browser;
    private bool _disposed;

    public PuppeteerFormRendererService(
        ILogger<PuppeteerFormRendererService> logger,
        IConfiguration configuration)
    {
        _logger = logger;
        // FormRendererBaseUrl overrides FrontendBaseUrl for container-to-container networking.
        // Inside Docker, FrontendBaseUrl points to the host (localhost:4200) but PuppeteerSharp
        // runs inside the API container and needs the Docker service name (forge-ui:80).
        _frontendBaseUrl = configuration["FormRendererBaseUrl"]
                           ?? configuration["FrontendBaseUrl"]
                           ?? "http://localhost:4200";
    }

    public async Task<List<byte[]>> RenderFormPagesAsync(string formDefinitionJson, CancellationToken ct)
    {
        _logger.LogInformation("Rendering form definition via headless Angular route ({Length} chars)",
            formDefinitionJson.Length);

        var browser = await GetBrowserAsync(ct);
        await using var page = await browser.NewPageAsync();

        try
        {
            await page.SetViewportAsync(new ViewPortOptions
            {
                Width = 900,
                Height = 1200,
                DeviceScaleFactor = 1,
            });

            // Navigate to the headless render route
            var url = $"{_frontendBaseUrl.TrimEnd('/')}/__render-form";
            _logger.LogDebug("Navigating to {Url}", url);
            await page.GoToAsync(url, new NavigationOptions
            {
                WaitUntil = [WaitUntilNavigation.DOMContentLoaded],
                Timeout = 30_000,
            });

            // Wait for Angular to bootstrap
            await page.WaitForSelectorAsync("app-root",
                new WaitForSelectorOptions { Timeout = 15_000 });

            // Wait for Angular routing + component init
            await Task.Delay(3000, ct);

            // Verify the headless render component is present
            var hasComponent = await page.EvaluateExpressionAsync<bool>(
                "!!document.querySelector('app-headless-form-render')");
            if (!hasComponent)
            {
                var bodyHtml = await page.EvaluateExpressionAsync<string>(
                    "document.body?.innerHTML?.substring(0, 500) ?? 'no body'");
                _logger.LogWarning("Headless render component not found. Body HTML: {Html}", bodyHtml);
            }

            // Inject the form definition
            var escapedJson = JsonSerializer.Serialize(formDefinitionJson);
            await page.EvaluateExpressionAsync(
                $"window.__FORM_DEFINITION__ = {escapedJson}; window.dispatchEvent(new Event('formDefinitionReady'));");

            // Wait for render completion
            await page.WaitForExpressionAsync("window.__RENDER_READY__ === true",
                new WaitForFunctionOptions { Timeout = 30_000 });

            var pageCount = await page.EvaluateExpressionAsync<int>("window.__PAGE_COUNT__ || 1");
            _logger.LogInformation("Form rendered: {PageCount} page(s), capturing screenshots", pageCount);
            await Task.Delay(1000, ct); // Allow render to fully settle

            var screenshots = new List<byte[]>();

            for (var i = 0; i < pageCount; i++)
            {
                if (i > 0)
                {
                    await page.EvaluateExpressionAsync($"window.__switchPage__({i})");
                    await Task.Delay(500, ct);
                }

                byte[] screenshot;
                try
                {
                    var containerHandle = await page.QuerySelectorAsync(".headless-render__container");
                    if (containerHandle != null)
                    {
                        screenshot = await containerHandle.ScreenshotDataAsync(new ElementScreenshotOptions
                        {
                            Type = ScreenshotType.Png,
                        });
                    }
                    else
                    {
                        screenshot = await page.ScreenshotDataAsync(new ScreenshotOptions
                        {
                            Type = ScreenshotType.Png,
                            FullPage = true,
                        });
                    }
                }
                catch (PuppeteerException ex)
                {
                    _logger.LogWarning(ex, "Element screenshot failed for page {Page}, falling back to full page", i + 1);
                    screenshot = await page.ScreenshotDataAsync(new ScreenshotOptions
                    {
                        Type = ScreenshotType.Png,
                        FullPage = true,
                    });
                }

                screenshots.Add(screenshot);
                _logger.LogDebug("Captured page {Page}/{Total} ({Size} bytes)", i + 1, pageCount, screenshot.Length);
            }

            _logger.LogInformation("Rendered {Count} page screenshot(s)", screenshots.Count);
            return screenshots;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogError(ex, "Form rendering failed");
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

            _logger.LogInformation("Launching headless Chromium for form rendering");

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

            _logger.LogInformation("Headless Chromium launched for form renderer (PID: {Pid})",
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
