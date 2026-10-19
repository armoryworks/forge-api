using System.Text.Json;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

using Forge.Core.Entities;
using Forge.Core.Enums;
using Forge.Core.Interfaces;
using Forge.Data.Context;

namespace Forge.Data.Services;

public class ApprovalService(AppDbContext db, ILogger<ApprovalService> logger) : IApprovalService
{
    private const string ExpenseSelfApprovalKey = "expense_self_approval";

    public async Task<ApprovalRequest?> SubmitForApprovalAsync(
        string entityType, int entityId, int requestedById,
        decimal? amount, string? entitySummary, CancellationToken ct)
    {
        var workflow = await FindMatchingWorkflowAsync(entityType, amount, ct);
        if (workflow == null)
            return null;

        var firstStep = workflow.Steps.OrderBy(s => s.StepNumber).First();

        if (firstStep.AutoApproveBelow.HasValue && amount.HasValue && amount.Value < firstStep.AutoApproveBelow.Value)
        {
            var autoRequest = new ApprovalRequest
            {
                WorkflowId = workflow.Id,
                EntityType = entityType,
                EntityId = entityId,
                CurrentStepNumber = firstStep.StepNumber,
                Status = ApprovalRequestStatus.AutoApproved,
                RequestedById = requestedById,
                RequestedAt = DateTimeOffset.UtcNow,
                Amount = amount,
                EntitySummary = entitySummary,
                CompletedAt = DateTimeOffset.UtcNow,
            };
            db.ApprovalRequests.Add(autoRequest);
            await db.SaveChangesAsync(ct);
            logger.LogInformation("Auto-approved {EntityType} {EntityId} (amount {Amount} below threshold {Threshold})",
                entityType, entityId, amount, firstStep.AutoApproveBelow);
            return autoRequest;
        }

        var request = new ApprovalRequest
        {
            WorkflowId = workflow.Id,
            EntityType = entityType,
            EntityId = entityId,
            CurrentStepNumber = firstStep.StepNumber,
            Status = ApprovalRequestStatus.Pending,
            RequestedById = requestedById,
            RequestedAt = DateTimeOffset.UtcNow,
            Amount = amount,
            EntitySummary = entitySummary,
        };

        db.ApprovalRequests.Add(request);
        await db.SaveChangesAsync(ct);

        logger.LogInformation("Submitted {EntityType} {EntityId} for approval via workflow '{WorkflowName}'",
            entityType, entityId, workflow.Name);

        return request;
    }

    public async Task<ApprovalRequest> ApproveAsync(int requestId, int decidedById, string? comments, CancellationToken ct)
    {
        var request = await GetRequestWithDetailsAsync(requestId, ct);
        ValidatePendingStatus(request);

        var decision = new ApprovalDecision
        {
            RequestId = requestId,
            StepNumber = request.CurrentStepNumber,
            DecidedById = decidedById,
            Decision = ApprovalDecisionType.Approve,
            Comments = comments?.Trim(),
            DecidedAt = DateTimeOffset.UtcNow,
        };
        db.ApprovalDecisions.Add(decision);

        var nextStep = request.Workflow.Steps
            .Where(s => s.StepNumber > request.CurrentStepNumber)
            .OrderBy(s => s.StepNumber)
            .FirstOrDefault();

        if (nextStep != null)
        {
            request.CurrentStepNumber = nextStep.StepNumber;

            if (nextStep.AutoApproveBelow.HasValue && request.Amount.HasValue
                && request.Amount.Value < nextStep.AutoApproveBelow.Value)
            {
                var autoDecision = new ApprovalDecision
                {
                    RequestId = requestId,
                    StepNumber = nextStep.StepNumber,
                    DecidedById = decidedById,
                    Decision = ApprovalDecisionType.Approve,
                    Comments = "Auto-approved (below threshold)",
                    DecidedAt = DateTimeOffset.UtcNow,
                };
                db.ApprovalDecisions.Add(autoDecision);
                request.Status = ApprovalRequestStatus.Approved;
                request.CompletedAt = DateTimeOffset.UtcNow;
            }
        }
        else
        {
            request.Status = ApprovalRequestStatus.Approved;
            request.CompletedAt = DateTimeOffset.UtcNow;
        }

        await db.SaveChangesAsync(ct);
        return request;
    }

    public async Task<ApprovalRequest> RejectAsync(int requestId, int decidedById, string comments, CancellationToken ct)
    {
        var request = await GetRequestWithDetailsAsync(requestId, ct);
        ValidatePendingStatus(request);

        var decision = new ApprovalDecision
        {
            RequestId = requestId,
            StepNumber = request.CurrentStepNumber,
            DecidedById = decidedById,
            Decision = ApprovalDecisionType.Reject,
            Comments = comments.Trim(),
            DecidedAt = DateTimeOffset.UtcNow,
        };
        db.ApprovalDecisions.Add(decision);

        request.Status = ApprovalRequestStatus.Rejected;
        request.CompletedAt = DateTimeOffset.UtcNow;

        await db.SaveChangesAsync(ct);
        return request;
    }

    public async Task<ApprovalRequest> DelegateAsync(int requestId, int decidedById, int delegateToUserId, string? comments, CancellationToken ct)
    {
        var request = await GetRequestWithDetailsAsync(requestId, ct);
        ValidatePendingStatus(request);

        var currentStep = request.Workflow.Steps.FirstOrDefault(s => s.StepNumber == request.CurrentStepNumber);
        if (currentStep != null && !currentStep.AllowDelegation)
            throw new InvalidOperationException("Delegation is not allowed for this approval step.");

        var decision = new ApprovalDecision
        {
            RequestId = requestId,
            StepNumber = request.CurrentStepNumber,
            DecidedById = decidedById,
            Decision = ApprovalDecisionType.Delegate,
            Comments = comments?.Trim(),
            DecidedAt = DateTimeOffset.UtcNow,
            DelegatedToUserId = delegateToUserId,
        };
        db.ApprovalDecisions.Add(decision);

        await db.SaveChangesAsync(ct);
        return request;
    }

    public async Task<bool> IsApprovalRequiredAsync(string entityType, decimal? amount, CancellationToken ct)
    {
        var workflow = await FindMatchingWorkflowAsync(entityType, amount, ct);
        return workflow != null;
    }

    public async Task<IReadOnlyList<ApprovalRequest>> GetPendingApprovalsAsync(int userId, CancellationToken ct)
    {
        var user = await db.Users.AsNoTracking()
            .FirstOrDefaultAsync(u => u.Id == userId, ct);

        if (user == null) return [];

        var userRoles = await GetUserRolesAsync(userId, ct);
        var expenseSelfApproval = await IsExpenseSelfApprovalAllowedAsync(ct);

        var pendingRequests = await db.ApprovalRequests
            .AsNoTracking()
            .Include(r => r.Workflow).ThenInclude(w => w.Steps)
            .Include(r => r.Decisions)
            .Where(r => r.Status == ApprovalRequestStatus.Pending
                     || r.Status == ApprovalRequestStatus.Escalated)
            .OrderBy(r => r.RequestedAt)
            .ToListAsync(ct);

        return pendingRequests
            .Where(r => ResolveActorStatus(r, userId, userRoles, expenseSelfApproval) == ApprovalActorStatus.Allowed)
            .ToList();
    }

    public async Task<ApprovalActorStatus> GetActorStatusAsync(int requestId, int userId, CancellationToken ct)
    {
        var request = await db.ApprovalRequests
            .AsNoTracking()
            .Include(r => r.Workflow).ThenInclude(w => w.Steps)
            .Include(r => r.Decisions)
            .FirstOrDefaultAsync(r => r.Id == requestId, ct)
            ?? throw new KeyNotFoundException($"Approval request {requestId} not found.");

        var userRoles = await GetUserRolesAsync(userId, ct);
        var expenseSelfApproval = await IsExpenseSelfApprovalAllowedAsync(ct);
        return ResolveActorStatus(request, userId, userRoles, expenseSelfApproval);
    }

    public async Task<IReadOnlyList<int>> GetCurrentApproverIdsAsync(int requestId, CancellationToken ct)
    {
        var request = await db.ApprovalRequests
            .AsNoTracking()
            .Include(r => r.Workflow).ThenInclude(w => w.Steps)
            .Include(r => r.Decisions)
            .FirstOrDefaultAsync(r => r.Id == requestId, ct)
            ?? throw new KeyNotFoundException($"Approval request {requestId} not found.");

        var currentStep = request.Workflow.Steps.FirstOrDefault(s => s.StepNumber == request.CurrentStepNumber);
        if (currentStep == null) return [];

        var candidateIds = new HashSet<int>(request.Decisions
            .Where(d => d.Decision == ApprovalDecisionType.Delegate
                     && d.StepNumber == request.CurrentStepNumber
                     && d.DelegatedToUserId.HasValue)
            .Select(d => d.DelegatedToUserId!.Value));

        if (currentStep.ApproverType == ApproverType.SpecificUser && currentStep.ApproverUserId.HasValue)
            candidateIds.Add(currentStep.ApproverUserId.Value);

        if (currentStep.ApproverType == ApproverType.Role && currentStep.ApproverRole != null)
        {
            var roleMembers = await db.UserRoles.AsNoTracking()
                .Join(db.Roles, ur => ur.RoleId, r => r.Id, (ur, r) => new { ur.UserId, r.Name })
                .Where(x => x.Name == currentStep.ApproverRole)
                .Select(x => x.UserId)
                .ToListAsync(ct);
            candidateIds.UnionWith(roleMembers);
        }

        var activeIds = await db.Users.AsNoTracking()
            .Where(u => candidateIds.Contains(u.Id) && u.IsActive)
            .Select(u => u.Id)
            .ToListAsync(ct);

        var expenseSelfApproval = await IsExpenseSelfApprovalAllowedAsync(ct);
        return activeIds
            .Where(id => id != request.RequestedById || SelfApprovalAllowed(request, expenseSelfApproval))
            .OrderBy(id => id)
            .ToList();
    }

    public async Task<IReadOnlyList<ApprovalRequest>> CheckEscalationsAsync(CancellationToken ct)
    {
        var now = DateTimeOffset.UtcNow;
        var pendingRequests = await db.ApprovalRequests
            .Include(r => r.Workflow).ThenInclude(w => w.Steps)
            .Where(r => r.Status == ApprovalRequestStatus.Pending
                     || r.Status == ApprovalRequestStatus.Escalated)
            .ToListAsync(ct);

        var escalated = new List<ApprovalRequest>();

        foreach (var request in pendingRequests)
        {
            var currentStep = request.Workflow.Steps
                .FirstOrDefault(s => s.StepNumber == request.CurrentStepNumber);

            if (currentStep?.EscalationHours == null) continue;

            var deadline = request.EscalatedAt ?? request.RequestedAt;
            if ((now - deadline).TotalHours < currentStep.EscalationHours.Value) continue;

            var nextStep = request.Workflow.Steps
                .Where(s => s.StepNumber > request.CurrentStepNumber)
                .OrderBy(s => s.StepNumber)
                .FirstOrDefault();

            if (nextStep != null)
            {
                var escalation = new ApprovalDecision
                {
                    RequestId = request.Id,
                    StepNumber = request.CurrentStepNumber,
                    DecidedById = request.RequestedById,
                    Decision = ApprovalDecisionType.Escalate,
                    Comments = $"Auto-escalated after {currentStep.EscalationHours}h",
                    DecidedAt = now,
                };
                db.ApprovalDecisions.Add(escalation);

                request.CurrentStepNumber = nextStep.StepNumber;
                request.Status = ApprovalRequestStatus.Escalated;
                request.EscalatedAt = now;
                escalated.Add(request);

                logger.LogInformation("Escalated approval request {RequestId} from step {FromStep} to {ToStep}",
                    request.Id, currentStep.StepNumber, nextStep.StepNumber);
            }
        }

        await db.SaveChangesAsync(ct);
        return escalated;
    }

    private async Task<List<string>> GetUserRolesAsync(int userId, CancellationToken ct)
    {
        return await db.UserRoles.AsNoTracking()
            .Where(ur => ur.UserId == userId)
            .Join(db.Roles, ur => ur.RoleId, r => r.Id, (_, r) => r.Name!)
            .ToListAsync(ct);
    }

    private async Task<bool> IsExpenseSelfApprovalAllowedAsync(CancellationToken ct)
    {
        var value = await db.SystemSettings.AsNoTracking()
            .Where(s => s.Key == ExpenseSelfApprovalKey)
            .Select(s => s.Value)
            .FirstOrDefaultAsync(ct);
        return value == "true";
    }

    private static bool SelfApprovalAllowed(ApprovalRequest request, bool expenseSelfApproval) =>
        expenseSelfApproval && string.Equals(request.EntityType, "Expense", StringComparison.Ordinal);

    private static ApprovalActorStatus ResolveActorStatus(
        ApprovalRequest request, int userId, IReadOnlyCollection<string> userRoles, bool expenseSelfApproval)
    {
        if (!IsCurrentApprover(request, userId, userRoles))
            return ApprovalActorStatus.NotCurrentApprover;

        if (request.RequestedById == userId && !SelfApprovalAllowed(request, expenseSelfApproval))
            return ApprovalActorStatus.OwnRequest;

        return ApprovalActorStatus.Allowed;
    }

    private static bool IsCurrentApprover(ApprovalRequest request, int userId, IReadOnlyCollection<string> userRoles)
    {
        var currentStep = request.Workflow.Steps.FirstOrDefault(s => s.StepNumber == request.CurrentStepNumber);
        if (currentStep == null) return false;

        var delegatedToMe = request.Decisions
            .Any(d => d.Decision == ApprovalDecisionType.Delegate
                   && d.DelegatedToUserId == userId
                   && d.StepNumber == request.CurrentStepNumber);
        if (delegatedToMe) return true;

        return currentStep.ApproverType switch
        {
            ApproverType.SpecificUser => currentStep.ApproverUserId == userId,
            ApproverType.Role => currentStep.ApproverRole != null && userRoles.Contains(currentStep.ApproverRole),
            ApproverType.Manager => false, // Would need org hierarchy
            _ => false,
        };
    }

    private async Task<ApprovalWorkflow?> FindMatchingWorkflowAsync(string entityType, decimal? amount, CancellationToken ct)
    {
        var workflows = await db.ApprovalWorkflows
            .AsNoTracking()
            .Include(w => w.Steps)
            .Where(w => w.IsActive && w.EntityType == entityType)
            .OrderByDescending(w => w.CreatedAt)
            .ToListAsync(ct);

        foreach (var workflow in workflows)
        {
            if (string.IsNullOrEmpty(workflow.ActivationConditionsJson))
                return workflow;

            try
            {
                var conditions = JsonSerializer.Deserialize<ActivationConditions>(
                    workflow.ActivationConditionsJson,
                    new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

                if (conditions?.AmountGreaterThan.HasValue == true
                    && amount.HasValue
                    && amount.Value <= conditions.AmountGreaterThan.Value)
                    continue;

                return workflow;
            }
            catch
            {
                return workflow;
            }
        }

        return null;
    }

    private async Task<ApprovalRequest> GetRequestWithDetailsAsync(int requestId, CancellationToken ct)
    {
        return await db.ApprovalRequests
            .Include(r => r.Workflow).ThenInclude(w => w.Steps)
            .Include(r => r.Decisions)
            .FirstOrDefaultAsync(r => r.Id == requestId, ct)
            ?? throw new KeyNotFoundException($"Approval request {requestId} not found.");
    }

    private static void ValidatePendingStatus(ApprovalRequest request)
    {
        if (request.Status != ApprovalRequestStatus.Pending && request.Status != ApprovalRequestStatus.Escalated)
            throw new InvalidOperationException($"Cannot act on approval request with status '{request.Status}'.");
    }

    private sealed class ActivationConditions
    {
        public decimal? AmountGreaterThan { get; set; }
    }
}
