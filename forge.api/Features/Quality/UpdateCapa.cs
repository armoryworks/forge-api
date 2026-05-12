using System.Security.Claims;

using MediatR;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;

using Forge.Core.Interfaces;
using Forge.Core.Models;
using Forge.Data.Context;

namespace Forge.Api.Features.Quality;

public record UpdateCapaCommand(int Id, UpdateCapaRequestModel Request) : IRequest;

public class UpdateCapaHandler(
    AppDbContext db,
    IClock clock,
    IHttpContextAccessor httpContextAccessor)
    : IRequestHandler<UpdateCapaCommand>
{
    public async Task Handle(UpdateCapaCommand command, CancellationToken cancellationToken)
    {
        var capa = await db.CorrectiveActions
            .FirstOrDefaultAsync(c => c.Id == command.Id, cancellationToken)
            ?? throw new KeyNotFoundException($"CAPA {command.Id} not found");

        var req = command.Request;
        var userId = int.Parse(httpContextAccessor.HttpContext!.User.FindFirstValue(ClaimTypes.NameIdentifier)!);

        if (req.Title != null) capa.Title = req.Title;
        if (req.ProblemDescription != null) capa.ProblemDescription = req.ProblemDescription;
        if (req.ImpactDescription != null) capa.ImpactDescription = req.ImpactDescription;
        if (req.ContainmentAction != null) capa.ContainmentAction = req.ContainmentAction;
        if (req.CorrectiveActionDescription != null) capa.CorrectiveActionDescription = req.CorrectiveActionDescription;
        if (req.PreventiveAction != null) capa.PreventiveAction = req.PreventiveAction;
        if (req.OwnerId.HasValue) capa.OwnerId = req.OwnerId.Value;
        if (req.Priority.HasValue) capa.Priority = req.Priority.Value;
        if (req.DueDate.HasValue) capa.DueDate = req.DueDate.Value;

        // Root cause analysis fields
        if (req.RootCauseAnalysis != null)
        {
            capa.RootCauseAnalysis = req.RootCauseAnalysis;
            capa.RootCauseAnalyzedById = userId;
            capa.RootCauseCompletedAt = clock.UtcNow;
        }
        if (req.RootCauseMethod.HasValue) capa.RootCauseMethod = req.RootCauseMethod.Value;
        if (req.RootCauseMethodData != null) capa.RootCauseMethodData = req.RootCauseMethodData;

        // Verification fields
        if (req.VerificationMethod != null) capa.VerificationMethod = req.VerificationMethod;
        if (req.VerificationResult != null)
        {
            capa.VerificationResult = req.VerificationResult;
            capa.VerifiedById = userId;
            capa.VerificationDate = clock.UtcNow;
        }

        // Effectiveness fields
        if (req.EffectivenessResult != null)
        {
            capa.EffectivenessResult = req.EffectivenessResult;
            capa.EffectivenessCheckedById = userId;
            capa.EffectivenessCheckDate = clock.UtcNow;
        }
        if (req.IsEffective.HasValue) capa.IsEffective = req.IsEffective.Value;

        await db.SaveChangesAsync(cancellationToken);
    }
}
