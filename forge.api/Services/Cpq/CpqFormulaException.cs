namespace Forge.Api.Services.Cpq;

/// <summary>A configurator pricing formula that cannot be read or evaluated, with a message an admin can act on.</summary>
public sealed class CpqFormulaException(string message) : Exception(message);
