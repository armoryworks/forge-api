using Riok.Mapperly.Abstractions;

using Forge.Core.Entities;
using Forge.Core.Models;

namespace Forge.Api.Mappers;

[Mapper]
public static partial class JobMapper
{
    /// <summary>
    /// Maps a Job entity to a JobDetailResponseModel.
    /// Navigation properties (TrackType, CurrentStage, Customer, Assignee) must be loaded via Include.
    /// Fields that require user lookups (AssigneeInitials, AssigneeName, AssigneeColor) must be set manually.
    /// </summary>
    public static JobDetailResponseModel ToDetailModel(
        this Job job,
        string? assigneeInitials = null,
        string? assigneeName = null,
        string? assigneeColor = null)
    {
        return new JobDetailResponseModel(
            Id: job.Id,
            JobNumber: job.JobNumber,
            Title: job.Title,
            Description: job.Description,
            TrackTypeId: job.TrackTypeId,
            TrackTypeName: job.TrackType?.Name ?? string.Empty,
            CurrentStageId: job.CurrentStageId,
            StageName: job.CurrentStage?.Name ?? string.Empty,
            StageColor: job.CurrentStage?.Color ?? string.Empty,
            AssigneeId: job.AssigneeId,
            AssigneeInitials: assigneeInitials,
            AssigneeName: assigneeName,
            AssigneeColor: assigneeColor,
            Priority: job.Priority.ToString(),
            CustomerId: job.CustomerId,
            CustomerName: job.Customer?.Name,
            DueDate: job.DueDate,
            StartDate: job.StartDate,
            CompletedDate: job.CompletedDate,
            IsArchived: job.IsArchived,
            BoardPosition: job.BoardPosition,
            IterationCount: job.IterationCount,
            IterationNotes: job.IterationNotes,
            ExternalId: job.ExternalId,
            ExternalRef: job.ExternalRef,
            Provider: job.Provider,
            Disposition: job.Disposition?.ToString(),
            DispositionNotes: job.DispositionNotes,
            DispositionAt: job.DispositionAt,
            PartId: job.PartId,
            PartNumber: job.Part?.PartNumber,
            ParentJobId: job.ParentJobId,
            ParentJobNumber: job.ParentJob?.JobNumber,
            ChildJobCount: job.ChildJobs?.Count(c => c.DeletedAt == null) ?? 0,
            CreatedAt: job.CreatedAt,
            UpdatedAt: job.UpdatedAt,
            CoverPhotoUrl: null,
            RowVersion: job.Version,
            SalesOrderId: job.SalesOrderLine?.SalesOrderId,
            SalesOrderNumber: job.SalesOrderLine?.SalesOrder?.OrderNumber,
            PartRevision: job.PartRevision);
    }
}
