using MediatR;
using Microsoft.EntityFrameworkCore;

using Forge.Core.Enums;
using Forge.Core.Interfaces;
using Forge.Core.Models;
using Forge.Data.Context;

namespace Forge.Api.Features.Quality;

public record UpdatePpapElementCommand(int SubmissionId, int ElementNumber, UpdatePpapElementRequestModel Request) : IRequest<PpapElementResponseModel>;

public class UpdatePpapElementHandler(AppDbContext db, IClock clock)
    : IRequestHandler<UpdatePpapElementCommand, PpapElementResponseModel>
{
    public async Task<PpapElementResponseModel> Handle(
        UpdatePpapElementCommand command, CancellationToken cancellationToken)
    {
        var element = await db.PpapElements
            .FirstOrDefaultAsync(e => e.SubmissionId == command.SubmissionId && e.ElementNumber == command.ElementNumber, cancellationToken)
            ?? throw new KeyNotFoundException($"PPAP element {command.ElementNumber} not found for submission {command.SubmissionId}");

        var req = command.Request;

        if (req.Status.HasValue)
        {
            element.Status = req.Status.Value;
            if (req.Status.Value == PpapElementStatus.Complete && !element.CompletedAt.HasValue)
                element.CompletedAt = clock.UtcNow;
            else if (req.Status.Value != PpapElementStatus.Complete)
                element.CompletedAt = null;
        }

        if (req.Notes != null)
            element.Notes = req.Notes;
        if (req.AssignedToUserId.HasValue)
            element.AssignedToUserId = req.AssignedToUserId;

        // Auto-advance submission status if any element is in progress
        var submission = await db.PpapSubmissions
            .Include(s => s.Elements)
            .FirstAsync(s => s.Id == command.SubmissionId, cancellationToken);

        if (submission.Status == PpapStatus.Draft && submission.Elements.Any(e => e.Status != PpapElementStatus.NotStarted))
            submission.Status = PpapStatus.InProgress;

        await db.SaveChangesAsync(cancellationToken);

        string? assigneeName = null;
        if (element.AssignedToUserId.HasValue)
        {
            assigneeName = await db.Users
                .Where(u => u.Id == element.AssignedToUserId.Value)
                .Select(u => $"{u.LastName}, {u.FirstName}")
                .FirstOrDefaultAsync(cancellationToken);
        }

        return new PpapElementResponseModel
        {
            Id = element.Id,
            ElementNumber = element.ElementNumber,
            ElementName = element.ElementName,
            Status = element.Status,
            IsRequired = element.IsRequired,
            Notes = element.Notes,
            AssignedToName = assigneeName,
            CompletedAt = element.CompletedAt,
        };
    }
}
