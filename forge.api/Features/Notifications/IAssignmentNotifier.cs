namespace Forge.Api.Features.Notifications;

public interface IAssignmentNotifier
{
    Task JobAssignedAsync(int jobId, string jobNumber, string jobTitle, int assigneeId, CancellationToken ct);

    Task SubtaskAssignedAsync(int jobId, string subtaskText, int assigneeId, CancellationToken ct);

    Task LeadAssignedAsync(int leadId, string leadName, int ownerId, CancellationToken ct);

    Task ApprovalAssignedAsync(
        string entityType, int entityId, string? entitySummary, IReadOnlyCollection<int> approverIds, CancellationToken ct);
}
