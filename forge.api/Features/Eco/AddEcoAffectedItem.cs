using System.Text.Json;

using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;

using Forge.Core.Entities;
using Forge.Core.Models;
using Forge.Data.Context;

namespace Forge.Api.Features.Eco;

public record AddEcoAffectedItemCommand(int EcoId, CreateEcoAffectedItemRequestModel Request) : IRequest<EcoAffectedItemResponseModel>;

public class AddEcoAffectedItemValidator : AbstractValidator<AddEcoAffectedItemCommand>
{
    public static readonly IReadOnlyList<string> EntityTypes = ["Part", "BOM", "Operation", "Drawing", "Specification"];

    public AddEcoAffectedItemValidator(AppDbContext db)
    {
        RuleFor(x => x.Request.EntityType)
            .NotEmpty()
            .Must(t => EntityTypes.Contains(t))
            .WithMessage($"Entity type must be one of: {string.Join(", ", EntityTypes)}");
        RuleFor(x => x.Request.EntityId).GreaterThan(0);
        RuleFor(x => x.Request)
            .MustAsync((r, ct) => EntityExistsAsync(db, r.EntityType, r.EntityId, ct))
            .When(x => EntityTypes.Contains(x.Request.EntityType) && x.Request.EntityId > 0)
            .WithName("EntityId")
            .WithMessage(x => $"{x.Request.EntityType} {x.Request.EntityId} does not exist");
        RuleFor(x => x.Request.ChangeDescription).NotEmpty().MaximumLength(500);
        RuleFor(x => x.Request.OldValue).Must(BeJson).WithMessage("Old value must be valid JSON");
        RuleFor(x => x.Request.NewValue).Must(BeJson).WithMessage("New value must be valid JSON");
    }

    private static bool BeJson(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return true;
        try
        {
            using var _ = JsonDocument.Parse(value);
            return true;
        }
        catch (JsonException)
        {
            return false;
        }
    }

    private static Task<bool> EntityExistsAsync(AppDbContext db, string entityType, int id, CancellationToken ct) =>
        entityType switch
        {
            "Part" or "BOM" => db.Parts.AnyAsync(p => p.Id == id, ct),
            "Operation" => db.Operations.AnyAsync(o => o.Id == id, ct),
            _ => db.FileAttachments.AnyAsync(f => f.Id == id, ct),
        };
}

public class AddEcoAffectedItemHandler(AppDbContext db) : IRequestHandler<AddEcoAffectedItemCommand, EcoAffectedItemResponseModel>
{
    public async Task<EcoAffectedItemResponseModel> Handle(AddEcoAffectedItemCommand request, CancellationToken cancellationToken)
    {
        var eco = await db.EngineeringChangeOrders
            .FirstOrDefaultAsync(e => e.Id == request.EcoId, cancellationToken)
            ?? throw new KeyNotFoundException($"ECO {request.EcoId} not found");

        var item = new EcoAffectedItem
        {
            EcoId = request.EcoId,
            EntityType = request.Request.EntityType,
            EntityId = request.Request.EntityId,
            ChangeDescription = request.Request.ChangeDescription,
            OldValue = string.IsNullOrWhiteSpace(request.Request.OldValue) ? null : request.Request.OldValue,
            NewValue = string.IsNullOrWhiteSpace(request.Request.NewValue) ? null : request.Request.NewValue,
        };

        db.EcoAffectedItems.Add(item);
        await db.SaveChangesAsync(cancellationToken);

        return new EcoAffectedItemResponseModel
        {
            Id = item.Id,
            EntityType = item.EntityType,
            EntityId = item.EntityId,
            ChangeDescription = item.ChangeDescription,
            OldValue = item.OldValue,
            NewValue = item.NewValue,
            IsImplemented = item.IsImplemented,
        };
    }
}
