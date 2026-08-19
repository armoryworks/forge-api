namespace Forge.Core.Models;

/// <summary>Rework: reset <paramref name="TargetStepKey"/> and everything downstream of it to Pending.</summary>
public record SequenceReworkRequestModel(string TargetStepKey, string Reason);
