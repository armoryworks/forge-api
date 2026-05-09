namespace QBEngineer.Core.Settings;

public static class AiSettings
{
    public const string Group = "AI — Ollama";

    public const string KeyMode = "ai.mode";
    public const string KeyBaseUrl = "ai.base-url";
    public const string KeyChatModel = "ai.chat-model";
    public const string KeyEmbeddingModel = "ai.embedding-model";

    public static IReadOnlyList<SettingDescriptor> Descriptors =>
    [
        new(KeyMode, Group, "Mode", SettingDataType.Enum,
            DefaultValue: IntegrationModeChoices.Mock,
            Description: "Mock returns canned text — useful when no Ollama instance is reachable.",
            Choices: IntegrationModeChoices.All, SortOrder: 0),
        new(KeyBaseUrl, Group, "Base URL", SettingDataType.Url,
            DefaultValue: "http://localhost:11434",
            Description: "Ollama HTTP endpoint. In Docker compose, usually http://qb-engineer-ai:11434.",
            SortOrder: 10),
        new(KeyChatModel, Group, "Chat Model", SettingDataType.String,
            DefaultValue: "gemma3:4b",
            Description: "Ollama model name for /generate calls.",
            SortOrder: 11),
        new(KeyEmbeddingModel, Group, "Embedding Model", SettingDataType.String,
            DefaultValue: "nomic-embed-text",
            Description: "Ollama model for vector embeddings (RAG indexing).",
            SortOrder: 12),
    ];
}
