using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;

using Microsoft.Extensions.Logging;

using Forge.Core.Interfaces;
using Forge.Core.Models;
using Forge.Core.Settings;

namespace Forge.Integrations;

/// <summary>
/// Real implementation of <see cref="IAiService"/> backed by Ollama.
/// Phase 1m: BaseUrl + model names + timeouts read live from
/// <see cref="ISettingsService"/> at request time. Each call composes
/// the absolute URL itself rather than using HttpClient.BaseAddress
/// (which is immutable after first use).
/// </summary>
public class OllamaAiService(
    HttpClient httpClient,
    ISettingsService settings,
    ILogger<OllamaAiService> logger) : IAiService
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    public Task<string> GenerateTextAsync(string prompt, CancellationToken ct)
        => GenerateTextAsync(prompt, null, null, ct);

    public async Task<string> GenerateTextAsync(string prompt, string? systemPrompt, double? temperature, CancellationToken ct)
    {
        var c = await ReadAsync(ct);
        logger.LogInformation("Ollama GenerateText ({Model}): {Prompt}",
            c.ChatModel, prompt.Length > 80 ? prompt[..80] + "..." : prompt);

        var request = new OllamaGenerateRequest
        {
            Model = c.ChatModel,
            Prompt = prompt,
            Stream = false,
            System = systemPrompt,
            Options = temperature.HasValue ? new OllamaGenerateOptions { Temperature = temperature.Value } : null,
        };

        using var timed = LinkedTimeout(ct, c.TimeoutSeconds);
        var response = await httpClient.PostAsJsonAsync(c.BaseUrl + "/api/generate", request, JsonOptions, timed.Token);
        response.EnsureSuccessStatusCode();

        var result = await response.Content.ReadFromJsonAsync<OllamaGenerateResponse>(JsonOptions, timed.Token);
        return result?.Response ?? string.Empty;
    }

    public async Task<string> SummarizeAsync(string text, CancellationToken ct)
    {
        var prompt = $"""
            Summarize the following text concisely in 2-3 sentences. Focus on the key facts and actionable information.

            Text:
            {text}

            Summary:
            """;

        return await GenerateTextAsync(prompt, ct);
    }

    public async Task<List<AiSearchResult>> SmartSearchAsync(string naturalLanguageQuery, CancellationToken ct)
    {
        logger.LogInformation("Ollama SmartSearch: {Query}", naturalLanguageQuery);

        var prompt = $"""
            You are a search assistant for a manufacturing operations platform. Given a natural language query, extract the most relevant search keywords.
            Return ONLY a JSON array of keyword strings, nothing else. Example: ["keyword1", "keyword2"]

            Query: {naturalLanguageQuery}
            """;

        var response = await GenerateTextAsync(prompt, ct);
        logger.LogInformation("Ollama SmartSearch keywords: {Response}", response);
        return [];
    }

    public async Task<float[]> GetEmbeddingAsync(string text, CancellationToken ct)
    {
        var c = await ReadAsync(ct);
        logger.LogInformation("Ollama GetEmbedding ({Length} chars)", text.Length);

        var request = new OllamaEmbeddingRequest
        {
            Model = c.EmbeddingModel,
            Prompt = text,
        };

        using var timed = LinkedTimeout(ct, c.TimeoutSeconds);
        var response = await httpClient.PostAsJsonAsync(c.BaseUrl + "/api/embeddings", request, JsonOptions, timed.Token);
        response.EnsureSuccessStatusCode();

        var result = await response.Content.ReadFromJsonAsync<OllamaEmbeddingResponse>(JsonOptions, timed.Token);
        return result?.Embedding ?? [];
    }

    public async Task<string> GenerateWithImageAsync(string prompt, byte[] imageBytes, string? systemPrompt, CancellationToken ct)
    {
        var c = await ReadAsync(ct);
        if (string.IsNullOrEmpty(c.VisionModel))
            throw new NotSupportedException("No vision model configured (Admin → Integrations → AI → Vision Model)");

        logger.LogInformation("Ollama GenerateWithImage ({Model}): prompt={PromptLen} chars, image={ImageSize} bytes",
            c.VisionModel, prompt.Length, imageBytes.Length);

        var imageBase64 = Convert.ToBase64String(imageBytes);

        var request = new OllamaVisionRequest
        {
            Model = c.VisionModel,
            Prompt = prompt,
            Stream = false,
            System = systemPrompt,
            Images = [imageBase64],
        };

        using var visionCts = LinkedTimeout(ct, c.VisionTimeoutSeconds);
        var httpRequestMessage = new HttpRequestMessage(HttpMethod.Post, c.BaseUrl + "/api/generate")
        {
            Content = JsonContent.Create(request, options: JsonOptions),
        };

        var response = await httpClient.SendAsync(httpRequestMessage, HttpCompletionOption.ResponseContentRead, visionCts.Token);
        response.EnsureSuccessStatusCode();

        var result = await response.Content.ReadFromJsonAsync<OllamaGenerateResponse>(JsonOptions, visionCts.Token);
        return result?.Response ?? string.Empty;
    }

    public async IAsyncEnumerable<string> GenerateTextStreamAsync(string prompt, [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct)
    {
        var c = await ReadAsync(ct);
        logger.LogInformation("Ollama GenerateTextStream ({Model}): {Prompt}",
            c.ChatModel, prompt.Length > 80 ? prompt[..80] + "..." : prompt);

        var request = new OllamaGenerateRequest
        {
            Model = c.ChatModel,
            Prompt = prompt,
            Stream = true,
        };

        var httpRequest = new HttpRequestMessage(HttpMethod.Post, c.BaseUrl + "/api/generate")
        {
            Content = JsonContent.Create(request, options: JsonOptions),
        };

        using var response = await httpClient.SendAsync(httpRequest, HttpCompletionOption.ResponseHeadersRead, ct);
        response.EnsureSuccessStatusCode();

        await using var stream = await response.Content.ReadAsStreamAsync(ct);
        using var reader = new System.IO.StreamReader(stream);

        while (!reader.EndOfStream && !ct.IsCancellationRequested)
        {
            var line = await reader.ReadLineAsync(ct);
            if (string.IsNullOrWhiteSpace(line)) continue;

            OllamaGenerateResponse? chunk = null;
            try
            {
                chunk = JsonSerializer.Deserialize<OllamaGenerateResponse>(line, JsonOptions);
            }
            catch (JsonException ex)
            {
                logger.LogWarning(ex, "Failed to parse Ollama stream line: {Line}", line);
                continue;
            }

            if (chunk is null) continue;

            if (!string.IsNullOrEmpty(chunk.Response))
                yield return chunk.Response;

            if (chunk.Done) break;
        }
    }

    public async Task<bool> IsAvailableAsync(CancellationToken ct)
    {
        try
        {
            var c = await ReadAsync(ct);
            using var timed = LinkedTimeout(ct, c.TimeoutSeconds);
            var response = await httpClient.GetAsync(c.BaseUrl + "/api/tags", timed.Token);
            return response.IsSuccessStatusCode;
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Ollama health check failed");
            return false;
        }
    }

    private async Task<AiResolved> ReadAsync(CancellationToken ct)
    {
        var baseUrl = await settings.GetStringAsync(AiSettings.KeyBaseUrl, ct) ?? "http://forge-ai:11434";
        var chat = await settings.GetStringAsync(AiSettings.KeyChatModel, ct) ?? "gemma3:4b";
        var emb = await settings.GetStringAsync(AiSettings.KeyEmbeddingModel, ct) ?? "all-minilm:l6-v2";
        var vis = await settings.GetStringAsync(AiSettings.KeyVisionModel, ct) ?? "llava:7b";
        var t1 = int.TryParse(await settings.GetStringAsync(AiSettings.KeyTimeoutSeconds, ct), out var t) ? t : 120;
        var t2 = int.TryParse(await settings.GetStringAsync(AiSettings.KeyVisionTimeoutSeconds, ct), out var vt) ? vt : 600;
        return new AiResolved(baseUrl.TrimEnd('/'), chat, emb, vis, t1, t2);
    }

    private static CancellationTokenSource LinkedTimeout(CancellationToken outer, int timeoutSeconds)
    {
        var cts = CancellationTokenSource.CreateLinkedTokenSource(outer);
        cts.CancelAfter(TimeSpan.FromSeconds(timeoutSeconds));
        return cts;
    }

    private sealed record AiResolved(
        string BaseUrl,
        string ChatModel,
        string EmbeddingModel,
        string? VisionModel,
        int TimeoutSeconds,
        int VisionTimeoutSeconds);

    // ─── Ollama API DTOs ───
    private sealed class OllamaGenerateRequest
    {
        public string Model { get; set; } = string.Empty;
        public string Prompt { get; set; } = string.Empty;
        public bool Stream { get; set; }
        public string? System { get; set; }
        public OllamaGenerateOptions? Options { get; set; }
    }

    private sealed class OllamaVisionRequest
    {
        public string Model { get; set; } = string.Empty;
        public string Prompt { get; set; } = string.Empty;
        public bool Stream { get; set; }
        public string? System { get; set; }
        public List<string> Images { get; set; } = [];
    }

    private sealed class OllamaGenerateOptions
    {
        public double Temperature { get; set; }
    }

    private sealed class OllamaGenerateResponse
    {
        public string Response { get; set; } = string.Empty;
        public bool Done { get; set; }
        public long TotalDuration { get; set; }
        public long LoadDuration { get; set; }
        public int PromptEvalCount { get; set; }
        public int EvalCount { get; set; }
    }

    private sealed class OllamaEmbeddingRequest
    {
        public string Model { get; set; } = string.Empty;
        public string Prompt { get; set; } = string.Empty;
    }

    private sealed class OllamaEmbeddingResponse
    {
        public float[] Embedding { get; set; } = [];
    }
}
