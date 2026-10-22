using Forge.Core.Enums;

namespace Forge.Core.Routing;

/// <summary>A job step's status and good count, as <see cref="StepGate"/> reads them.</summary>
public sealed record StepProgress(JobOperationStatus Status, decimal Completed);
