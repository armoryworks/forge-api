using Forge.Core.Models;

namespace Forge.Core.Interfaces;

public interface IAiService
{
    Task<string> GenerateTextAsync(string prompt, CancellationToken ct);
    Task<string> GenerateTextAsync(string prompt, string? systemPrompt, double? temperature, CancellationToken ct);
    Task<string> SummarizeAsync(string text, CancellationToken ct);
    Task<List<AiSearchResult>> SmartSearchAsync(string naturalLanguageQuery, CancellationToken ct);
    Task<float[]> GetEmbeddingAsync(string text, CancellationToken ct);
    Task<bool> IsAvailableAsync(CancellationToken ct);

    /// <summary>
    /// Generate text from a prompt with an accompanying image (multimodal).
    /// Used for visual verification of extracted form definitions against source PDF screenshots.
    /// Requires a vision-capable model (e.g., llava, llama3.2-vision).
    /// </summary>
    /// <returns>AI-generated text response</returns>
    /// <exception cref="NotSupportedException">Thrown when no vision model is configured</exception>
    Task<string> GenerateWithImageAsync(string prompt, byte[] imageBytes, string? systemPrompt, CancellationToken ct);

    /// <summary>
    /// Stream generated text token by token via Server-Sent Events.
    /// Each yielded string is a partial token from the model.
    /// </summary>
    IAsyncEnumerable<string> GenerateTextStreamAsync(string prompt, CancellationToken ct);
}
