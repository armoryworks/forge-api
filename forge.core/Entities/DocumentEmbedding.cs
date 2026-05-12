using Pgvector;

namespace Forge.Core.Entities;

public class DocumentEmbedding : BaseAuditableEntity
{
    public string EntityType { get; set; } = string.Empty;
    public int EntityId { get; set; }
    public string ChunkText { get; set; } = string.Empty;
    public int ChunkIndex { get; set; }
    public string? SourceField { get; set; }
    public Vector? Embedding { get; set; }
    public string ModelName { get; set; } = string.Empty;
}
