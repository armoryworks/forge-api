using System.Security.Claims;
using FluentValidation;
using MediatR;
using Forge.Api.Services;
using Forge.Core.Entities;
using Forge.Core.Interfaces;
using Forge.Core.Models;

namespace Forge.Api.Features.Parts;

public record CreateBOMEntryCommand(int ParentPartId, CreateBOMEntryRequestModel Data) : IRequest<PartDetailResponseModel>;

public class CreateBOMEntryCommandValidator : AbstractValidator<CreateBOMEntryCommand>
{
    public CreateBOMEntryCommandValidator()
    {
        RuleFor(x => x.ParentPartId).GreaterThan(0);
        RuleFor(x => x.Data.ChildPartId).GreaterThan(0);
        RuleFor(x => x.Data.Quantity).GreaterThan(0);
    }
}

public class CreateBOMEntryHandler(
    IPartRepository repo,
    IBomRevisionService bomRevisions,
    IHttpContextAccessor httpContext) : IRequestHandler<CreateBOMEntryCommand, PartDetailResponseModel>
{
    public async Task<PartDetailResponseModel> Handle(CreateBOMEntryCommand request, CancellationToken cancellationToken)
    {
        var parent = await repo.FindAsync(request.ParentPartId, cancellationToken)
            ?? throw new KeyNotFoundException($"Part {request.ParentPartId} not found");

        if (request.Data.ChildPartId == request.ParentPartId)
            throw new InvalidOperationException("A part cannot reference itself in its BOM");

        var child = await repo.FindAsync(request.Data.ChildPartId, cancellationToken)
            ?? throw new KeyNotFoundException($"Child part {request.Data.ChildPartId} not found");

        var maxSort = await repo.GetMaxBomSortOrderAsync(request.ParentPartId, cancellationToken);

        var entry = new BOMEntry
        {
            ParentPartId = request.ParentPartId,
            ChildPartId = request.Data.ChildPartId,
            Quantity = request.Data.Quantity,
            ReferenceDesignator = request.Data.ReferenceDesignator?.Trim(),
            SortOrder = maxSort + 1,
            SourceType = request.Data.SourceType,
            LeadTimeDays = request.Data.LeadTimeDays,
            Notes = request.Data.Notes?.Trim(),
        };

        await repo.AddBomEntryAsync(entry, cancellationToken);

        // Phase 3 H4 / WU-20 — adding a component is a structural change;
        // capture an immutable revision snapshot of the new state.
        var userId = TryGetUserId(httpContext);
        await bomRevisions.CaptureCurrentStateAsync(request.ParentPartId, userId, "Component added", cancellationToken);

        return (await repo.GetDetailAsync(request.ParentPartId, cancellationToken))!;
    }

    private static int? TryGetUserId(IHttpContextAccessor http)
    {
        var raw = http.HttpContext?.User.FindFirstValue(ClaimTypes.NameIdentifier);
        return int.TryParse(raw, out var v) ? v : (int?)null;
    }
}
