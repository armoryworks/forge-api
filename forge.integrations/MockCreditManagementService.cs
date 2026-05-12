using Microsoft.Extensions.Logging;

using Forge.Core.Enums;
using Forge.Core.Interfaces;
using Forge.Core.Models;

namespace Forge.Integrations;

public class MockCreditManagementService(ILogger<MockCreditManagementService> logger) : ICreditManagementService
{
    public Task<CreditStatusResponseModel> GetCreditStatusAsync(int customerId, CancellationToken ct)
    {
        logger.LogInformation("MockCreditManagementService: GetCreditStatus for customer {CustomerId}", customerId);

        return Task.FromResult(new CreditStatusResponseModel
        {
            CustomerId = customerId,
            CustomerName = $"Customer {customerId}",
            CreditLimit = 50000m,
            OpenArBalance = 12000m,
            PendingOrdersTotal = 8000m,
            TotalExposure = 20000m,
            AvailableCredit = 30000m,
            UtilizationPercent = 40m,
            IsOnHold = false,
            HoldReason = null,
            IsOverLimit = false,
            RiskLevel = CreditRisk.Low,
        });
    }

    public Task<bool> CheckCreditForOrderAsync(int customerId, decimal orderAmount, CancellationToken ct)
    {
        logger.LogInformation("MockCreditManagementService: CheckCreditForOrder customer {CustomerId}, amount={Amount}", customerId, orderAmount);
        return Task.FromResult(true);
    }

    public Task PlaceHoldAsync(int customerId, int placedById, string reason, CancellationToken ct)
    {
        logger.LogInformation("MockCreditManagementService: PlaceHold on customer {CustomerId} by user {UserId}", customerId, placedById);
        return Task.CompletedTask;
    }

    public Task ReleaseHoldAsync(int customerId, int releasedById, string? releaseNotes, CancellationToken ct)
    {
        logger.LogInformation("MockCreditManagementService: ReleaseHold on customer {CustomerId} by user {UserId}", customerId, releasedById);
        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<CreditStatusResponseModel>> GetCreditRiskReportAsync(CancellationToken ct)
    {
        logger.LogInformation("MockCreditManagementService: GetCreditRiskReport");

        IReadOnlyList<CreditStatusResponseModel> result = new List<CreditStatusResponseModel>
        {
            new()
            {
                CustomerId = 1,
                CustomerName = "Acme Corp",
                CreditLimit = 100000m,
                OpenArBalance = 85000m,
                PendingOrdersTotal = 20000m,
                TotalExposure = 105000m,
                AvailableCredit = -5000m,
                UtilizationPercent = 105m,
                IsOnHold = false,
                IsOverLimit = true,
                RiskLevel = CreditRisk.High,
            },
        };

        return Task.FromResult(result);
    }

    public Task CheckCreditReviewsDueAsync(CancellationToken ct)
    {
        logger.LogInformation("MockCreditManagementService: CheckCreditReviewsDue — no-op in mock mode");
        return Task.CompletedTask;
    }
}
