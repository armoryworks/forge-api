using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;

using Forge.Core.Entities;
using Forge.Core.Interfaces;
using Forge.Core.Models;
using Forge.Data.Context;

namespace Forge.Api.Features.Quality;

public record CreateCapaCommand(CreateCapaRequestModel Request) : IRequest<CapaResponseModel>;

public class CreateCapaValidator : AbstractValidator<CreateCapaCommand>
{
    public CreateCapaValidator()
    {
        RuleFor(x => x.Request.Title).NotEmpty().MaximumLength(500);
        RuleFor(x => x.Request.ProblemDescription).NotEmpty().MaximumLength(4000);
        RuleFor(x => x.Request.OwnerId).GreaterThan(0);
        RuleFor(x => x.Request.Priority).InclusiveBetween(1, 5);
    }
}

public class CreateCapaHandler(AppDbContext db, INcrCapaService ncrCapaService)
    : IRequestHandler<CreateCapaCommand, CapaResponseModel>
{
    public async Task<CapaResponseModel> Handle(
        CreateCapaCommand command, CancellationToken cancellationToken)
    {
        var req = command.Request;
        var capaNumber = await ncrCapaService.GenerateCapaNumberAsync(cancellationToken);

        var capa = new CorrectiveAction
        {
            CapaNumber = capaNumber,
            Type = req.Type,
            SourceType = req.SourceType,
            SourceEntityId = req.SourceEntityId,
            SourceEntityType = req.SourceEntityType,
            Title = req.Title,
            ProblemDescription = req.ProblemDescription,
            ImpactDescription = req.ImpactDescription,
            OwnerId = req.OwnerId,
            Priority = req.Priority,
            DueDate = req.DueDate,
        };

        db.CorrectiveActions.Add(capa);
        await db.SaveChangesAsync(cancellationToken);

        var ownerName = await db.Users
            .Where(u => u.Id == capa.OwnerId)
            .Select(u => $"{u.LastName}, {u.FirstName}")
            .FirstOrDefaultAsync(cancellationToken) ?? "Unknown";

        return new CapaResponseModel
        {
            Id = capa.Id,
            CapaNumber = capa.CapaNumber,
            Type = capa.Type,
            SourceType = capa.SourceType,
            SourceEntityId = capa.SourceEntityId,
            SourceEntityType = capa.SourceEntityType,
            Title = capa.Title,
            ProblemDescription = capa.ProblemDescription,
            ImpactDescription = capa.ImpactDescription,
            OwnerId = capa.OwnerId,
            OwnerName = ownerName,
            Status = capa.Status,
            Priority = capa.Priority,
            DueDate = capa.DueDate,
            TaskCount = 0,
            CompletedTaskCount = 0,
            RelatedNcrCount = 0,
            CreatedAt = capa.CreatedAt,
        };
    }
}
