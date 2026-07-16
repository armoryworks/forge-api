namespace Forge.Core.Models;

/// <summary>Closes out a recall (marks it Resolved) with optional resolution notes.</summary>
public record ResolveRecallRequestModel(string? ResolutionNotes);
