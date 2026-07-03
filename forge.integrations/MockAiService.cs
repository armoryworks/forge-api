using Microsoft.Extensions.Logging;

using Forge.Core.Interfaces;

namespace Forge.Integrations;

public class MockAiService : IAiService
{
    private readonly ILogger<MockAiService> _logger;

    public MockAiService(ILogger<MockAiService> logger)
    {
        _logger = logger;
    }

    public Task<string> GenerateTextAsync(string prompt, CancellationToken ct)
        => GenerateTextAsync(prompt, null, null, ct);

    public Task<string> GenerateTextAsync(string prompt, string? systemPrompt, double? temperature, CancellationToken ct)
    {
        var truncated = prompt.Length > 80 ? prompt[..80] + "..." : prompt;
        _logger.LogInformation("[MockAI] GenerateText: {Prompt}", truncated);
        return Task.FromResult($"[Mock AI] Generated response for: {truncated}");
    }

    public Task<string> SummarizeAsync(string text, CancellationToken ct)
    {
        var summary = text.Length > 200 ? text[..200] + "..." : text;
        _logger.LogInformation("[MockAI] Summarize ({Length} chars)", text.Length);
        return Task.FromResult(summary);
    }

    public Task<float[]> GetEmbeddingAsync(string text, CancellationToken ct)
    {
        _logger.LogInformation("[MockAI] GetEmbedding ({Length} chars)", text.Length);
        return Task.FromResult(new float[384]);
    }

    public Task<string> GenerateWithImageAsync(string prompt, byte[] imageBytes, string? systemPrompt, CancellationToken ct)
    {
        _logger.LogInformation("[MockAI] GenerateWithImage: prompt={PromptLen} chars, image={ImageSize} bytes",
            prompt.Length, imageBytes.Length);
        return Task.FromResult("""
            {
              "layoutMatch": true,
              "issues": [],
              "corrections": null,
              "confidence": 0.95
            }
            """);
    }

    public async IAsyncEnumerable<string> GenerateTextStreamAsync(string prompt, [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct)
    {
        _logger.LogInformation("[MockAI] GenerateTextStream: streaming canned response");
        var words = new[]
        {
            "This", " is", " a", " mock", " streaming", " response", " from", " the",
            " AI", " assistant.", " It", " simulates", " token-by-token", " generation",
            " so", " the", " UI", " can", " render", " partial", " results", " in", " real", " time.",
        };
        foreach (var word in words)
        {
            if (ct.IsCancellationRequested) yield break;
            await Task.Delay(30, ct);
            yield return word;
        }
    }

    public Task<bool> IsAvailableAsync(CancellationToken ct)
    {
        _logger.LogInformation("[MockAI] IsAvailable — returning true");
        return Task.FromResult(true);
    }
}
