using Forge.Core.Models;

namespace Forge.Core.Interfaces;

public interface ICreditManagementService
{
    Task<CreditStatusResponseModel> GetCreditStatusAsync(int customerId, CancellationToken ct);
    Task<bool> CheckCreditForOrderAsync(int customerId, decimal orderAmount, CancellationToken ct);
    Task PlaceHoldAsync(int customerId, int placedById, string reason, CancellationToken ct);
    Task ReleaseHoldAsync(int customerId, int releasedById, string? releaseNotes, CancellationToken ct);
    Task<IReadOnlyList<CreditStatusResponseModel>> GetCreditRiskReportAsync(CancellationToken ct);
    Task CheckCreditReviewsDueAsync(CancellationToken ct);
}
