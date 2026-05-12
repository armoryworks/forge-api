using System.Security.Claims;
using FluentValidation;
using MediatR;
using Forge.Api.Services;
using Forge.Core.Interfaces;
using Forge.Core.Models;

namespace Forge.Api.Features.Parts;

public record UpdateBOMEntryCommand(int ParentPartId, int BomEntryId, UpdateBOMEntryRequestModel Data) : IRequest<PartDetailResponseModel>;

public class UpdateBOMEntryValidator : AbstractValidator<UpdateBOMEntryCommand>
{
    public UpdateBOMEntryValidator()
    {
        RuleFor(x => x.ParentPartId).GreaterThan(0);
        RuleFor(x => x.BomEntryId).GreaterThan(0);
        RuleFor(x => x.Data.Quantity).GreaterThan(0).When(x => x.Data.Quantity.HasValue);
        RuleFor(x => x.Data.ReferenceDesignator).MaximumLength(200).When(x => x.Data.ReferenceDesignator is not null);
        RuleFor(x => x.Data.Notes).MaximumLength(2000).When(x => x.Data.Notes is not null);
    }
}

public class UpdateBOMEntryHandler(
    IPartRepository repo,
    IBomRevisionService bomRevisions,
    IHttpContextAccessor httpContext) : IRequestHandler<UpdateBOMEntryCommand, PartDetailResponseModel>
{
    public async Task<PartDetailResponseModel> Handle(UpdateBOMEntryCommand request, CancellationToken cancellationToken)
    {
        var entry = await repo.FindBomEntryAsync(request.BomEntryId, request.ParentPartId, cancellationToken)
            ?? throw new KeyNotFoundException($"BOM entry {request.BomEntryId} not found on part {request.ParentPartId}");

        var data = request.Data;

        // Phase 3 H4 / WU-20 — detect whether this is a *structural* change
        // (qty / source type / leadTime / referenceDesignator) vs purely a
        // notes edit. Notes-only edits do NOT auto-revision (per WU spec:
        // "Metadata change … edits in-place"). Anything that materially
        // changes what gets consumed when the BOM is exploded does.
        var structuralChange = false;
        if (data.Quantity.HasValue && entry.Quantity != data.Quantity.Value) structuralChange = true;
        if (data.SourceType.HasValue && entry.SourceType != data.SourceType.Value) structuralChange = true;
        if (data.LeadTimeDays is not null && entry.LeadTimeDays != data.LeadTimeDays) structuralChange = true;
        if (data.ReferenceDesignator is not null
            && (entry.ReferenceDesignator ?? "") != data.ReferenceDesignator.Trim())
            structuralChange = true;

        if (data.Quantity.HasValue) entry.Quantity = data.Quantity.Value;
        if (data.ReferenceDesignator is not null) entry.ReferenceDesignator = data.ReferenceDesignator.Trim();
        if (data.SourceType.HasValue) entry.SourceType = data.SourceType.Value;
        if (data.LeadTimeDays is not null) entry.LeadTimeDays = data.LeadTimeDays;
        if (data.Notes is not null) entry.Notes = data.Notes.Trim();

        await repo.SaveChangesAsync(cancellationToken);

        if (structuralChange)
        {
            var userId = int.TryParse(httpContext.HttpContext?.User.FindFirstValue(ClaimTypes.NameIdentifier), out var v) ? v : (int?)null;
            await bomRevisions.CaptureCurrentStateAsync(request.ParentPartId, userId, "Component edited", cancellationToken);
        }

        return (await repo.GetDetailAsync(request.ParentPartId, cancellationToken))!;
    }
}
