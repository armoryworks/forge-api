namespace Forge.Core.Models;

public record CopqTrendPointResponseModel
{
    public DateOnly Period { get; init; }
    public decimal InternalFailure { get; init; }
    public decimal ExternalFailure { get; init; }
    public decimal Appraisal { get; init; }
    public decimal Prevention { get; init; }
    public decimal Total { get; init; }
}
