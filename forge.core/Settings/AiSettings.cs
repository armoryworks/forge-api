namespace Forge.Core.Settings;

public static class AiSettings
{
    public const string Group = "AI — Ollama";

    public const string KeyMode = "ai.mode";
    public const string KeyBaseUrl = "ai.base-url";
    public const string KeyChatModel = "ai.chat-model";
    public const string KeyEmbeddingModel = "ai.embedding-model";
    public const string KeyVisionModel = "ai.vision-model";
    public const string KeyTimeoutSeconds = "ai.timeout-seconds";
    public const string KeyVisionTimeoutSeconds = "ai.vision-timeout-seconds";
    public const string KeyDocsPath = "ai.docs-path";

    public static IReadOnlyList<SettingDescriptor> Descriptors =>
    [
        new(KeyMode, Group, "Mode", SettingDataType.Enum,
            DefaultValue: IntegrationModeChoices.Mock,
            Description: "Mock returns canned text — useful when no Ollama instance is reachable.",
            Choices: IntegrationModeChoices.All, SortOrder: 0),
        new(KeyBaseUrl, Group, "Base URL", SettingDataType.Url,
            DefaultValue: "http://forge-ai:11434",
            Description: "Ollama HTTP endpoint. In Docker compose, usually http://forge-ai:11434.",
            SortOrder: 10),
        new(KeyChatModel, Group, "Chat Model", SettingDataType.String,
            DefaultValue: "gemma3:4b",
            Description: "Ollama model name for /generate calls.",
            SortOrder: 11),
        new(KeyEmbeddingModel, Group, "Embedding Model", SettingDataType.String,
            DefaultValue: "all-minilm:l6-v2",
            Description: "Ollama model for vector embeddings (RAG indexing).",
            SortOrder: 12),
        new(KeyVisionModel, Group, "Vision Model", SettingDataType.String,
            DefaultValue: "llava:7b",
            Description: "Ollama vision-language model for image / receipt OCR + Q&A.",
            SortOrder: 13),
        new(KeyTimeoutSeconds, Group, "Timeout (seconds)", SettingDataType.Integer,
            DefaultValue: "120",
            SortOrder: 14),
        new(KeyVisionTimeoutSeconds, Group, "Vision Timeout (seconds)", SettingDataType.Integer,
            DefaultValue: "600",
            Description: "Vision calls process images and run noticeably slower.",
            SortOrder: 15),
        new(KeyDocsPath, Group, "Documentation Path", SettingDataType.String,
            DefaultValue: "/app/docs",
            Description: "Filesystem path the documentation-indexing job reads markdown from to build the RAG index. Default /app/docs matches the Docker image layout — set this when docs are mounted elsewhere.",
            SortOrder: 20),
    ];
}
