namespace Forge.Core.Models;

public record LaborRateResponseModel
{
    public int Id { get; init; }
    public int UserId { get; init; }
    public decimal StandardRatePerHour { get; init; }
    public decimal OvertimeRatePerHour { get; init; }
    public decimal? DoubletimeRatePerHour { get; init; }
    public DateOnly EffectiveFrom { get; init; }
    public DateOnly? EffectiveTo { get; init; }
    public string? Notes { get; init; }
}
