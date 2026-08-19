namespace Forge.Core.Models;

/// <summary>Payload for actions that require a stated reason (cancel, skip, override).</summary>
public record SequenceReasonRequestModel(string Reason);
