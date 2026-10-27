using System.Runtime.CompilerServices;
using System.Text;

using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

using Forge.Api.Features.Ai;
using Forge.Core.Settings;

namespace Forge.Tests.Ai;

public class AiSseStreamWriterTests
{
    private static AiSseStreamWriter Writer(string timeoutSeconds = "1")
    {
        var settings = new Mock<ISettingsService>();
        settings.Setup(s => s.GetStringAsync(AiSettings.KeyTimeoutSeconds, It.IsAny<CancellationToken>())).ReturnsAsync(timeoutSeconds);
        return new AiSseStreamWriter(settings.Object, NullLogger<AiSseStreamWriter>.Instance);
    }

    private static async Task<string> RunAsync(IAsyncEnumerable<string> tokens)
    {
        var context = new DefaultHttpContext();
        var body = new MemoryStream();
        context.Response.Body = body;

        await Writer().WriteAsync(context.Response, tokens, CancellationToken.None);

        context.Response.ContentType.Should().Be("text/event-stream");
        return Encoding.UTF8.GetString(body.ToArray());
    }

    private static async IAsyncEnumerable<string> Tokens(params string[] tokens)
    {
        foreach (var token in tokens)
        {
            await Task.Yield();
            yield return token;
        }
    }

    private static async IAsyncEnumerable<string> StallsAfter(string first, [EnumeratorCancellation] CancellationToken ct = default)
    {
        yield return first;
        await Task.Delay(Timeout.Infinite, ct);
        yield return "never";
    }

    private static async IAsyncEnumerable<string> FailsAfter(string first)
    {
        yield return first;
        await Task.Yield();
        throw new HttpRequestException("connection reset");
    }

    private static async IAsyncEnumerable<string> ModelTimesOut(string first)
    {
        yield return first;
        await Task.Yield();
        throw new TimeoutException("The AI model did not respond within 1 seconds.");
    }

    [Fact]
    public async Task A_complete_answer_streams_every_token_then_done()
    {
        var output = await RunAsync(Tokens("Hel", "lo\nthere"));

        output.Should().Be("data: Hel\n\ndata: lo\\nthere\n\ndata: [DONE]\n\n");
    }

    [Fact]
    public async Task A_stall_after_the_first_token_ends_with_an_error_event_and_keeps_the_partial_answer()
    {
        var output = await RunAsync(StallsAfter("Hel")).WaitAsync(TimeSpan.FromSeconds(15));

        output.Should().StartWith("data: Hel\n\n");
        output.Should().EndWith("event: error\ndata: {\"code\":\"ai-stream-stalled\"}\n\n");
        output.Should().NotContain("[DONE]");
    }

    [Fact]
    public async Task A_model_side_timeout_is_reported_as_a_stall()
    {
        var output = await RunAsync(ModelTimesOut("Hel"));

        output.Should().EndWith("event: error\ndata: {\"code\":\"ai-stream-stalled\"}\n\n");
    }

    [Fact]
    public async Task A_broken_stream_ends_with_a_failure_event_instead_of_an_aborted_connection()
    {
        var output = await RunAsync(FailsAfter("Hel"));

        output.Should().Be("data: Hel\n\nevent: error\ndata: {\"code\":\"ai-stream-failed\"}\n\n");
    }
}
