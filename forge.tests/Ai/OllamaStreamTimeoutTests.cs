using System.Diagnostics;
using System.IO.Pipelines;
using System.Net;
using System.Text;

using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

using Forge.Core.Settings;
using Forge.Integrations;

namespace Forge.Tests.Ai;

public class OllamaStreamTimeoutTests
{
    private sealed class DelayingHandler(Func<CancellationToken, Task<HttpResponseMessage>> respond) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            respond(cancellationToken);
    }

    private static OllamaAiService Service(HttpMessageHandler handler)
    {
        var settings = new Mock<ISettingsService>();
        settings.Setup(s => s.GetStringAsync(It.IsAny<string>(), It.IsAny<CancellationToken>())).ReturnsAsync((string?)null);
        settings.Setup(s => s.GetStringAsync(AiSettings.KeyTimeoutSeconds, It.IsAny<CancellationToken>())).ReturnsAsync("1");
        return new OllamaAiService(new HttpClient(handler), settings.Object, NullLogger<OllamaAiService>.Instance);
    }

    private static async Task<List<string>> DrainAsync(IAsyncEnumerable<string> stream, List<string> received)
    {
        await foreach (var token in stream)
            received.Add(token);
        return received;
    }

    [Fact]
    public async Task A_model_that_never_answers_ends_the_stream_with_a_timeout()
    {
        var service = Service(new DelayingHandler(async ct =>
        {
            await Task.Delay(Timeout.Infinite, ct);
            return new HttpResponseMessage(HttpStatusCode.OK);
        }));
        var watch = Stopwatch.StartNew();

        var act = () => DrainAsync(service.GenerateTextStreamAsync("hi", CancellationToken.None), []);

        await act.Should().ThrowAsync<TimeoutException>().WithMessage("*1 seconds*");
        watch.Elapsed.Should().BeLessThan(TimeSpan.FromSeconds(10));
    }

    [Fact]
    public async Task A_model_that_stalls_mid_answer_ends_the_stream_with_a_timeout_after_the_tokens_it_sent()
    {
        var pipe = new Pipe();
        await pipe.Writer.WriteAsync(Encoding.UTF8.GetBytes("{\"response\":\"Hel\",\"done\":false}\n{\"response\":\"lo\",\"done\":false}\n"));
        var service = Service(new DelayingHandler(_ => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StreamContent(pipe.Reader.AsStream()),
        })));
        var received = new List<string>();

        var act = () => DrainAsync(service.GenerateTextStreamAsync("hi", CancellationToken.None), received);

        await act.Should().ThrowAsync<TimeoutException>();
        received.Should().Equal("Hel", "lo");
    }

    [Fact]
    public async Task A_steady_stream_slower_overall_than_the_timeout_is_not_cut_off()
    {
        var pipe = new Pipe();
        var service = Service(new DelayingHandler(_ => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StreamContent(pipe.Reader.AsStream()),
        })));
        var writer = Task.Run(async () =>
        {
            foreach (var part in new[] { "a", "b", "c", "d" })
            {
                await pipe.Writer.WriteAsync(Encoding.UTF8.GetBytes($"{{\"response\":\"{part}\",\"done\":false}}\n"));
                await Task.Delay(400);
            }
            await pipe.Writer.WriteAsync(Encoding.UTF8.GetBytes("{\"response\":\"\",\"done\":true}\n"));
            await pipe.Writer.CompleteAsync();
        });

        var received = await DrainAsync(service.GenerateTextStreamAsync("hi", CancellationToken.None), []);
        await writer;

        received.Should().Equal("a", "b", "c", "d");
    }

    [Fact]
    public async Task A_caller_cancel_is_a_cancel_not_a_timeout()
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(100));
        var service = Service(new DelayingHandler(async ct =>
        {
            await Task.Delay(Timeout.Infinite, ct);
            return new HttpResponseMessage(HttpStatusCode.OK);
        }));

        var act = () => DrainAsync(service.GenerateTextStreamAsync("hi", cts.Token), []);

        await act.Should().ThrowAsync<OperationCanceledException>();
    }
}
