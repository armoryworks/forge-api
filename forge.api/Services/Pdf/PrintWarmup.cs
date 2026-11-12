using System.Diagnostics;

using Forge.Core.Interfaces;

namespace Forge.Api.Services.Pdf;

/// <summary>
/// Prints one blank page shortly after the API starts. The first print after a new image starts reads the
/// print engine and every installed font from a cold disk, which can take half a minute; doing it here keeps
/// that wait off the first person who opens an invoice. It also says in the log, at startup, whether
/// documents can be printed on this host at all.
/// </summary>
public class PrintWarmup(IHtmlPdfRenderer renderer, IHostEnvironment environment, ILogger<PrintWarmup> log) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        if (environment.IsEnvironment("Testing"))
            return;

        try
        {
            await Task.Delay(TimeSpan.FromSeconds(5), ct);
            var started = Stopwatch.GetTimestamp();
            await renderer.RenderAsync(PdfHtml.Document("Warm-up", Html.Empty), ct);
            log.LogInformation("Document printing is ready (first print took {Elapsed:0} ms)", Stopwatch.GetElapsedTime(started).TotalMilliseconds);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
        }
        catch (Exception ex)
        {
            log.LogWarning(ex, "Documents cannot be printed on this host: {Reason}", ex.Message);
        }
    }
}
