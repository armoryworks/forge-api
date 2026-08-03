namespace Forge.Core.Models;

/// <summary>
/// A candidate existing part whose name is trigram-similar to a proposed new
/// name. Powers the near-duplicate guard shown before a part is created.
/// Score is the pg_trgm similarity (0..1); higher = closer.
/// </summary>
public record PartSimilarityResultModel(
    int Id,
    string PartNumber,
    string Name,
    double Score);
