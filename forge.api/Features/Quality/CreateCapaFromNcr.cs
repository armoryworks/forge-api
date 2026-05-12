using MediatR;
using Microsoft.EntityFrameworkCore;

using Forge.Core.Interfaces;
using Forge.Core.Models;
using Forge.Data.Context;

namespace Forge.Api.Features.Quality;

public record CreateCapaFromNcrCommand(int NcrId, int OwnerId) : IRequest<CapaResponseModel>;

public class CreateCapaFromNcrHandler(AppDbContext db, INcrCapaService ncrCapaService)
    : IRequestHandler<CreateCapaFromNcrCommand, CapaResponseModel>
{
    public async Task<CapaResponseModel> Handle(
        CreateCapaFromNcrCommand command, CancellationToken cancellationToken)
    {
        var capa = await ncrCapaService.CreateCapaFromNcrAsync(
            command.NcrId, command.OwnerId, cancellationToken);

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
            OwnerId = capa.OwnerId,
            OwnerName = ownerName,
            Status = capa.Status,
            Priority = capa.Priority,
            DueDate = capa.DueDate,
            TaskCount = 0,
            CompletedTaskCount = 0,
            RelatedNcrCount = 1,
            CreatedAt = capa.CreatedAt,
        };
    }
}
