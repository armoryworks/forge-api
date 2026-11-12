using System.ComponentModel;
using System.Diagnostics;
using System.Text;

using Microsoft.Extensions.Logging;

using Forge.Core.Interfaces;
using Forge.Core.Models;

namespace Forge.Integrations;

/// <summary>
/// Prints HTML documents to PDF with WeasyPrint, run as one child process per document. WeasyPrint has no
/// script engine, and the fetcher it is given refuses every URL that is not inline data, so a document
/// cannot run code, reach the network or read a file whatever it contains. A document split into parts
/// prints its parts side by side, always leaving a process free for other documents, and joins the files.
/// </summary>
public class WeasyPrintPdfRenderer(ILogger<WeasyPrintPdfRenderer> logger) : IHtmlPdfRenderer, IDisposable
{
    /// <summary>Environment variable naming the Python that has WeasyPrint installed; <c>python3</c> when unset.</summary>
    public const string PythonVariable = "FORGE_PDF_PYTHON";

    private const int MaxConcurrentPrints = 4;
    private const int MaxConcurrentParts = MaxConcurrentPrints - 1;
    private const int ErrorTailLength = 2000;
    private static readonly TimeSpan PrintTimeout = TimeSpan.FromSeconds(60);

    private const string Script = """
        import sys
        from weasyprint import HTML, default_url_fetcher

        def fetch(url, timeout=10, ssl_context=None):
            if url.startswith("data:"):
                return default_url_fetcher(url, timeout, ssl_context)
            raise ValueError("a printed document cannot load " + url.split(":", 1)[0] + " resources")

        HTML(string=sys.stdin.buffer.read().decode("utf-8"), url_fetcher=fetch).write_pdf(sys.stdout.buffer)
        """;

    private readonly SemaphoreSlim _prints = new(MaxConcurrentPrints, MaxConcurrentPrints);

    public static string Python
        => Environment.GetEnvironmentVariable(PythonVariable) is { Length: > 0 } configured ? configured : "python3";

    public async Task<byte[]> RenderAsync(HtmlPdfDocument document, CancellationToken ct)
    {
        await _prints.WaitAsync(ct);
        try
        {
            using var process = Start();
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeout.CancelAfter(PrintTimeout);
            try
            {
                var errors = process.StandardError.ReadToEndAsync(timeout.Token);
                var output = ReadAllAsync(process.StandardOutput.BaseStream, timeout.Token);
                await process.StandardInput.BaseStream.WriteAsync(Encoding.UTF8.GetBytes(document.Html), timeout.Token);
                process.StandardInput.Close();

                var pdf = await output;
                await process.WaitForExitAsync(timeout.Token);
                var messages = await errors;
                if (process.ExitCode != 0 || pdf.Length == 0)
                {
                    logger.LogError("WeasyPrint exited with code {ExitCode}: {Messages}", process.ExitCode, Tail(messages));
                    throw new IOException("The document could not be printed.");
                }
                if (messages.Length > 0)
                    logger.LogDebug("WeasyPrint: {Messages}", Tail(messages));
                return pdf;
            }
            catch (OperationCanceledException)
            {
                Kill(process);
                ct.ThrowIfCancellationRequested();
                throw new TimeoutException($"Printing a document took longer than {PrintTimeout.TotalSeconds:0} seconds.");
            }
        }
        finally
        {
            _prints.Release();
        }
    }

    public async Task<byte[]> RenderAsync(IReadOnlyList<HtmlPdfDocument> parts, CancellationToken ct)
    {
        if (parts.Count == 0)
            throw new ArgumentException("A document needs at least one part.", nameof(parts));
        if (parts.Count == 1)
            return await RenderAsync(parts[0], ct);

        using var stop = CancellationTokenSource.CreateLinkedTokenSource(ct);
        using var turns = new SemaphoreSlim(MaxConcurrentParts, MaxConcurrentParts);
        var prints = parts.Select(part => RenderPartAsync(part, turns, stop)).ToList();
        return PdfJoiner.Join(await Task.WhenAll(prints));
    }

    private async Task<byte[]> RenderPartAsync(HtmlPdfDocument part, SemaphoreSlim turns, CancellationTokenSource stop)
    {
        await turns.WaitAsync(stop.Token);
        try
        {
            return await RenderAsync(part, stop.Token);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            await stop.CancelAsync();
            throw;
        }
        finally
        {
            turns.Release();
        }
    }

    private static Process Start()
    {
        var start = new ProcessStartInfo(Python)
        {
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardErrorEncoding = Encoding.UTF8,
            UseShellExecute = false,
        };
        start.ArgumentList.Add("-c");
        start.ArgumentList.Add(Script);

        var process = new Process { StartInfo = start };
        try
        {
            process.Start();
            return process;
        }
        catch (Win32Exception ex)
        {
            process.Dispose();
            throw new IOException(
                $"Documents cannot be printed: '{Python}' was not found. Install Python 3 with WeasyPrint, or set {PythonVariable}.", ex);
        }
    }

    private static async Task<byte[]> ReadAllAsync(Stream stream, CancellationToken ct)
    {
        using var buffer = new MemoryStream();
        await stream.CopyToAsync(buffer, ct);
        return buffer.ToArray();
    }

    private static void Kill(Process process)
    {
        try
        {
            if (!process.HasExited)
                process.Kill(entireProcessTree: true);
        }
        catch (InvalidOperationException)
        {
        }
    }

    private static string Tail(string text) => text.Length <= ErrorTailLength ? text : text[^ErrorTailLength..];

    public void Dispose()
    {
        _prints.Dispose();
        GC.SuppressFinalize(this);
    }
}
