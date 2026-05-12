using Forge.Core.Models;

namespace Forge.Core.Interfaces;

public interface IOvertimeService
{
    Task<OvertimeBreakdownResponseModel> CalculateOvertimeAsync(int userId, DateOnly weekStart, DateOnly weekEnd, CancellationToken ct);
}
