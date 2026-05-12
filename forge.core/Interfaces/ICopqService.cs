using Forge.Core.Models;

namespace Forge.Core.Interfaces;

public interface ICopqService
{
    Task<CopqReportResponseModel> GenerateReportAsync(DateOnly periodStart, DateOnly periodEnd, CancellationToken ct);
    Task<IReadOnlyList<CopqTrendPointResponseModel>> GetTrendAsync(int months, CancellationToken ct);
    Task<IReadOnlyList<CopqParetoItemResponseModel>> GetParetoByDefectAsync(DateOnly periodStart, DateOnly periodEnd, CancellationToken ct);
}
