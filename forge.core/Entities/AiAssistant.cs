namespace Forge.Core.Entities;

public class AiAssistant : BaseAuditableEntity
{
    public string Name { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string Icon { get; set; } = "smart_toy";
    public string Color { get; set; } = "#0d9488";
    public string Category { get; set; } = "Custom";
    public string SystemPrompt { get; set; } = string.Empty;
    public string AllowedEntityTypes { get; set; } = "[]";
    public string StarterQuestions { get; set; } = "[]";
    public bool IsActive { get; set; } = true;
    public bool IsBuiltIn { get; set; }
    public int SortOrder { get; set; }
    public double Temperature { get; set; } = 0.7;
    public int MaxContextChunks { get; set; } = 5;
}
