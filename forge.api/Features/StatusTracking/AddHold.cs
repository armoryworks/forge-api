using FluentValidation;
using MediatR;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;

using Forge.Core.Entities;
using Forge.Core.Enums;
using Forge.Core.Interfaces;
using Forge.Core.Models;
using Forge.Data.Context;

namespace Forge.Api.Features.StatusTracking;

public record AddHoldCommand(
    string EntityType,
    int EntityId,
    AddHoldRequestModel Data) : IRequest<StatusEntryResponseModel>;

public class AddHoldCommandValidator : AbstractValidator<AddHoldCommand>
{
    public AddHoldCommandValidator()
    {
        RuleFor(x => x.EntityType).NotEmpty().MaximumLength(50);
        RuleFor(x => x.EntityId).GreaterThan(0);
        RuleFor(x => x.Data.StatusCode).NotEmpty().MaximumLength(50);
        RuleFor(x => x.Data.Notes).MaximumLength(2000).When(x => x.Data.Notes is not null);
    }
}

public class AddHoldHandler(
    AppDbContext db,
    IStatusEntryRepository repository,
    IActivityLogRepository activityRepo,
    IWorkCenterContext workCenterContext,
    IHttpContextAccessor httpContext)
    : IRequestHandler<AddHoldCommand, StatusEntryResponseModel>
{
    public async Task<StatusEntryResponseModel> Handle(
        AddHoldCommand request, CancellationToken cancellationToken)
    {
        // Check for duplicate active hold with same status code
        var existingHold = await db.StatusEntries
            .AnyAsync(s => s.EntityType == request.EntityType
                           && s.EntityId == request.EntityId
                           && s.Category == "hold"
                           && s.StatusCode == request.Data.StatusCode
                           && s.EndedAt == null, cancellationToken);

        if (existingHold)
        {
            throw new InvalidOperationException(
                $"An active hold with status code '{request.Data.StatusCode}' already exists for this entity.");
        }

        // Resolve label from reference_data (fallback to StatusCode if not found)
        var label = await db.ReferenceData
            .Where(r => r.Code == request.Data.StatusCode && r.IsActive)
            .Select(r => r.Label)
            .FirstOrDefaultAsync(cancellationToken) ?? request.Data.StatusCode;

        var userIdClaim = httpContext.HttpContext?.User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier);
        int? currentUserId = userIdClaim is not null ? int.Parse(userIdClaim.Value) : null;

        // Capture work-center context only for jobs — for non-job entities
        // (Customer credit hold, PO hold, etc.) the concept doesn't apply.
        int? workCenterId = null;
        int? operationId = null;
        if (string.Equals(request.EntityType, "job", System.StringComparison.OrdinalIgnoreCase))
        {
            (workCenterId, operationId) = await workCenterContext.ResolveForJobAsync(
                request.EntityId, currentUserId, cancellationToken);
        }

        var statusEntry = new StatusEntry
        {
            EntityType = request.EntityType,
            EntityId = request.EntityId,
            StatusCode = request.Data.StatusCode,
            StatusLabel = label,
            Category = "hold",
            StartedAt = DateTimeOffset.UtcNow,
            EndedAt = null,
            Notes = request.Data.Notes?.Trim(),
            WorkCenterId = workCenterId,
            OperationId = operationId,
        };

        await db.StatusEntries.AddAsync(statusEntry, cancellationToken);

        var description = $"Hold added: {label}.";

        if (string.Equals(request.EntityType, "job", System.StringComparison.OrdinalIgnoreCase))
        {
            await activityRepo.AddAsync(new JobActivityLog
            {
                JobId = request.EntityId,
                UserId = currentUserId,
                Action = ActivityAction.StatusChanged,
                FieldName = "Hold",
                OldValue = null,
                NewValue = label,
                Description = description,
                WorkCenterId = workCenterId,
                OperationId = operationId,
            }, cancellationToken);
        }
        else
        {
            await activityRepo.AddAsync(new ActivityLog
            {
                EntityType = request.EntityType,
                EntityId = request.EntityId,
                UserId = currentUserId,
                Action = ActivityAction.StatusChanged.ToString(),
                FieldName = "Hold",
                OldValue = null,
                NewValue = label,
                Description = description,
            }, cancellationToken);
        }

        await db.SaveChangesAsync(cancellationToken);

        // Reload to return with SetBy info
        var holds = await repository.GetActiveHoldsAsync(request.EntityType, request.EntityId, cancellationToken);
        return holds.First(h => h.Id == statusEntry.Id);
    }
}
