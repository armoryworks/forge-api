using MediatR;
using Microsoft.EntityFrameworkCore;

using Forge.Core.Models;
using Forge.Data.Context;

namespace Forge.Api.Features.Workflows.Validators;

/// <summary>Workflow Pattern Phase 3 — fetch one validator by id.</summary>
public record GetEntityValidatorQuery(int Id) : IRequest<EntityValidatorResponseModel>;

public class GetEntityValidatorHandler(AppDbContext db)
    : IRequestHandler<GetEntityValidatorQuery, EntityValidatorResponseModel>
{
    public async Task<EntityValidatorResponseModel> Handle(GetEntityValidatorQuery request, CancellationToken ct)
    {
        var row = await db.EntityReadinessValidators
            .AsNoTracking()
            .FirstOrDefaultAsync(v => v.Id == request.Id, ct)
            ?? throw new KeyNotFoundException($"Validator id {request.Id} not found.");

        return new EntityValidatorResponseModel(
            row.Id, row.EntityType, row.ValidatorId, row.Predicate,
            row.DisplayNameKey, row.MissingMessageKey, row.IsSeedData);
    }
}
