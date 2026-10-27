using System.Globalization;
using System.Text.Json;

using Forge.Core.Settings;

namespace Forge.Api.Features.Ai;

public sealed class AiSseStreamWriter(ISettingsService settings, ILogger<AiSseStreamWriter> logger)
{
    public const string StalledCode = "ai-stream-stalled";
    public const string FailedCode = "ai-stream-failed";
    private const int DefaultIdleSeconds = 120;
    private static readonly TimeSpan DrainGrace = TimeSpan.FromSeconds(5);

    public async Task WriteAsync(HttpResponse response, IAsyncEnumerable<string> tokens, CancellationToken ct)
    {
        var idle = await IdleLimitAsync(ct);

        response.Headers.ContentType = "text/event-stream";
        response.Headers.CacheControl = "no-cache";
        response.Headers["X-Accel-Buffering"] = "no";

        using var streamCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        var enumerator = tokens.GetAsyncEnumerator(streamCts.Token);
        var (errorCode, pending) = await PumpAsync(response, enumerator, idle, ct);

        if (pending is not null)
        {
            await streamCts.CancelAsync();
            await DrainAsync(enumerator, pending);
        }
        else
        {
            await enumerator.DisposeAsync();
        }

        if (errorCode is null)
        {
            await response.WriteAsync("data: [DONE]\n\n", ct);
        }
        else
        {
            var data = JsonSerializer.Serialize(new { code = errorCode });
            await response.WriteAsync($"event: error\ndata: {data}\n\n", ct);
        }
        await response.Body.FlushAsync(ct);
    }

    private async Task<(string? ErrorCode, Task<bool>? Pending)> PumpAsync(
        HttpResponse response, IAsyncEnumerator<string> enumerator, TimeSpan idle, CancellationToken ct)
    {
        while (true)
        {
            var next = enumerator.MoveNextAsync().AsTask();
            bool hasNext;
            try
            {
                hasNext = await next.WaitAsync(idle, ct);
            }
            catch (TimeoutException ex) when (!next.IsCompleted)
            {
                logger.LogWarning(ex, "AI stream stalled: no token within {Seconds}s", idle.TotalSeconds);
                return (StalledCode, next);
            }
            catch (TimeoutException ex)
            {
                logger.LogWarning(ex, "AI model stream timed out");
                return (StalledCode, null);
            }
            catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
            {
                logger.LogWarning(ex, "AI stream failed after the response started");
                return (FailedCode, null);
            }

            if (!hasNext) return (null, null);

            var escaped = enumerator.Current.Replace("\n", "\\n").Replace("\r", "\\r");
            await response.WriteAsync($"data: {escaped}\n\n", ct);
            await response.Body.FlushAsync(ct);
        }
    }

    private async Task DrainAsync(IAsyncEnumerator<string> enumerator, Task<bool> pending)
    {
        try
        {
            await pending.WaitAsync(DrainGrace);
        }
        catch (Exception ex)
        {
            logger.LogDebug(ex, "AI stream ended after the stall was reported");
        }

        if (!pending.IsCompleted) return;

        try
        {
            await enumerator.DisposeAsync();
        }
        catch (Exception ex)
        {
            logger.LogDebug(ex, "AI stream enumerator did not shut down cleanly after a stall");
        }
    }

    private async Task<TimeSpan> IdleLimitAsync(CancellationToken ct)
    {
        var raw = await settings.GetStringAsync(AiSettings.KeyTimeoutSeconds, ct);
        var seconds = int.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out var s) && s > 0
            ? s
            : DefaultIdleSeconds;
        return TimeSpan.FromSeconds(seconds);
    }
}
