using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Forge.Core.Entities;
using Forge.Core.Models;
using Forge.Data.Context;

namespace Forge.Api.Features.Admin;

public record CreateReferenceDataCommand(
    string GroupCode,
    string Code,
    string Label,
    int SortOrder,
    DateTimeOffset? EffectiveFrom,
    DateTimeOffset? EffectiveTo,
    string? Metadata) : IRequest<ReferenceDataResponseModel>;

public class CreateReferenceDataValidator : AbstractValidator<CreateReferenceDataCommand>
{
    public CreateReferenceDataValidator()
    {
        RuleFor(x => x.GroupCode).NotEmpty().MaximumLength(100);
        RuleFor(x => x.Code).NotEmpty().MaximumLength(100);
        RuleFor(x => x.Label).NotEmpty().MaximumLength(200);
        RuleFor(x => x.SortOrder).GreaterThanOrEqualTo(0);
    }
}

public class CreateReferenceDataHandler(AppDbContext db)
    : IRequestHandler<CreateReferenceDataCommand, ReferenceDataResponseModel>
{
    public async Task<ReferenceDataResponseModel> Handle(CreateReferenceDataCommand request, CancellationToken cancellationToken)
    {
        var exists = await db.ReferenceData
            .AnyAsync(r => r.GroupCode == request.GroupCode && r.Code == request.Code, cancellationToken);

        if (exists)
            throw new InvalidOperationException($"Reference data entry '{request.Code}' already exists in group '{request.GroupCode}'.");

        var entry = new Forge.Core.Entities.ReferenceData
        {
            GroupCode = request.GroupCode,
            Code = request.Code,
            Label = request.Label,
            SortOrder = request.SortOrder,
            EffectiveFrom = request.EffectiveFrom,
            EffectiveTo = request.EffectiveTo,
            Metadata = request.Metadata,
        };

        db.ReferenceData.Add(entry);
        await db.SaveChangesAsync(cancellationToken);

        return new ReferenceDataResponseModel(
            entry.Id, entry.Code, entry.Label, entry.SortOrder, entry.IsActive, entry.IsSeedData,
            entry.EffectiveFrom, entry.EffectiveTo, entry.Metadata, entry.ParentId);
    }
}
