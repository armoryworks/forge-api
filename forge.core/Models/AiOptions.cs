namespace Forge.Core.Models;

public class AiOptions
{
    public const string SectionName = "Ai";

    public string BaseUrl { get; set; } = "http://forge-ai:11434";
    public string Model { get; set; } = "gemma3:4b";
    public string EmbeddingModel { get; set; } = "all-minilm:l6-v2";
    public string VisionModel { get; set; } = "llava:7b";
    public int TimeoutSeconds { get; set; } = 120;
    public int VisionTimeoutSeconds { get; set; } = 600;
    public string DocsPath { get; set; } = "/app/docs";

    /// <summary>
    /// ai-fleet-orchestration D-2: optional per-client override directory. When set, its
    /// <c>.md</c> files shadow same-named baseline docs (client wins) in the RAG index.
    /// Empty = baseline docs only.
    /// </summary>
    public string ClientDocsPath { get; set; } = string.Empty;
}
