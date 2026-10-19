namespace Forge.Core.Models;

/// <summary>
/// Closes out a recall (marks it Resolved) with optional resolution notes. <see cref="ReleaseHeldStock"/> moves
/// the affected lots' quarantined stock back to Stored; it is off unless asked for, because a resolved recall
/// does not mean the held material is good.
/// </summary>
public record ResolveRecallRequestModel(string? ResolutionNotes, bool ReleaseHeldStock = false);
