using System.Security.Claims;
using FluentValidation;
using MediatR;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Forge.Core.Entities;
using Forge.Core.Models;
using Forge.Data.Context;

namespace Forge.Api.Features.Assets;

public record CreateDowntimeLogCommand(CreateDowntimeLogRequestModel Data) : IRequest<DowntimeLogResponseModel>;

public class CreateDowntimeLogValidator : AbstractValidator<CreateDowntimeLogCommand>
{
    public CreateDowntimeLogValidator()
    {
        RuleFor(x => x.Data.AssetId).GreaterThan(0);
        RuleFor(x => x.Data.Reason).NotEmpty().MaximumLength(500);
        RuleFor(x => x.Data.Resolution).MaximumLength(500);
        RuleFor(x => x.Data.Notes).MaximumLength(2000);
        RuleFor(x => x.Data.EndedAt)
            .GreaterThan(x => x.Data.StartedAt)
            .When(x => x.Data.EndedAt.HasValue)
            .WithMessage("End time must be after start time.");
    }
}

public class CreateDowntimeLogHandler(
    AppDbContext db,
    IHttpContextAccessor httpContext) : IRequestHandler<CreateDowntimeLogCommand, DowntimeLogResponseModel>
{
    public async Task<DowntimeLogResponseModel> Handle(CreateDowntimeLogCommand request, CancellationToken cancellationToken)
    {
        var asset = await db.Assets.FindAsync([request.Data.AssetId], cancellationToken)
            ?? throw new KeyNotFoundException($"Asset {request.Data.AssetId} not found.");

        var userId = int.Parse(httpContext.HttpContext!.User.FindFirstValue(ClaimTypes.NameIdentifier)!);

        var log = new DowntimeLog
        {
            AssetId = request.Data.AssetId,
            WorkCenterId = request.Data.WorkCenterId,
            ReportedById = userId,
            StartedAt = request.Data.StartedAt,
            EndedAt = request.Data.EndedAt,
            Category = request.Data.Category,
            DowntimeReasonId = request.Data.DowntimeReasonId,
            Reason = request.Data.Reason.Trim(),
            Resolution = request.Data.Resolution?.Trim(),
            Description = request.Data.Description?.Trim(),
            IsPlanned = request.Data.IsPlanned,
            JobId = request.Data.JobId,
            Notes = request.Data.Notes?.Trim(),
        };

        db.DowntimeLogs.Add(log);
        await db.SaveChangesAsync(cancellationToken);

        return new DowntimeLogResponseModel(
            log.Id, log.AssetId, asset.Name, log.ReportedById,
            log.StartedAt, log.EndedAt, log.Reason, log.Resolution,
            log.IsPlanned, log.Notes, log.DurationHours, log.CreatedAt);
    }
}
