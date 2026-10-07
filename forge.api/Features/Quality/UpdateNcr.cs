using System.Security.Claims;

using FluentValidation;
using FluentValidation.Results;
using MediatR;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;

using Forge.Core.Enums;
using Forge.Core.Interfaces;
using Forge.Core.Models;
using Forge.Data.Context;
using Forge.Data.Extensions;

namespace Forge.Api.Features.Quality;

public record UpdateNcrCommand(int Id, UpdateNcrRequestModel Request) : IRequest;

public class UpdateNcrValidator : AbstractValidator<UpdateNcrCommand>
{
    public UpdateNcrValidator()
    {
        RuleFor(x => x.Request.Type).IsInEnum().When(x => x.Request.Type.HasValue);
        RuleFor(x => x.Request.Description)
            .NotEmpty().MaximumLength(4000)
            .When(x => x.Request.Description != null);
        RuleFor(x => x.Request.AffectedQuantity)
            .GreaterThan(0)
            .When(x => x.Request.AffectedQuantity.HasValue);
        RuleFor(x => x.Request.DefectiveQuantity)
            .GreaterThanOrEqualTo(0)
            .When(x => x.Request.DefectiveQuantity.HasValue);
        RuleFor(x => x.Request.DefectiveQuantity)
            .LessThanOrEqualTo(x => x.Request.AffectedQuantity)
            .When(x => x.Request.DefectiveQuantity.HasValue && x.Request.AffectedQuantity.HasValue)
            .WithMessage("Defective quantity cannot exceed affected quantity");
        RuleFor(x => x.Request.MaterialCost)
            .GreaterThanOrEqualTo(0)
            .When(x => x.Request.MaterialCost.HasValue);
        RuleFor(x => x.Request.LaborCost)
            .GreaterThanOrEqualTo(0)
            .When(x => x.Request.LaborCost.HasValue);
    }
}

public class UpdateNcrHandler(
    AppDbContext db,
    IClock clock,
    IHttpContextAccessor httpContextAccessor)
    : IRequestHandler<UpdateNcrCommand>
{
    public async Task Handle(UpdateNcrCommand command, CancellationToken cancellationToken)
    {
        var ncr = await db.NonConformances
            .FirstOrDefaultAsync(n => n.Id == command.Id, cancellationToken)
            ?? throw new KeyNotFoundException($"NCR {command.Id} not found");

        var req = command.Request;

        if (ncr.Status >= NcrStatus.Dispositioned)
        {
            var locked = new List<string>();
            if (req.Type.HasValue && req.Type.Value != ncr.Type) locked.Add("type");
            if (req.Description != null && req.Description != ncr.Description) locked.Add("description");
            if (req.AffectedQuantity.HasValue && req.AffectedQuantity.Value != ncr.AffectedQuantity) locked.Add("affectedQuantity");
            if (req.DefectiveQuantity.HasValue && req.DefectiveQuantity != ncr.DefectiveQuantity) locked.Add("defectiveQuantity");
            if (locked.Count > 0)
                throw new InvalidOperationException(
                    $"NCR {ncr.NcrNumber} is {ncr.Status}; {string.Join(", ", locked)} can no longer be changed.");
        }

        var effectiveAffected = req.AffectedQuantity ?? ncr.AffectedQuantity;
        var effectiveDefective = req.DefectiveQuantity ?? ncr.DefectiveQuantity;
        if (effectiveDefective.HasValue && effectiveDefective.Value > effectiveAffected)
            throw new ValidationException(
            [
                new ValidationFailure("Request.DefectiveQuantity", "Defective quantity cannot exceed affected quantity"),
            ]);

        var changedFields = new List<string>();

        if (req.Type.HasValue && req.Type.Value != ncr.Type)
        {
            ncr.Type = req.Type.Value;
            changedFields.Add("type");
        }
        if (req.Description != null && req.Description != ncr.Description)
        {
            ncr.Description = req.Description;
            changedFields.Add("description");
        }
        if (req.AffectedQuantity.HasValue && req.AffectedQuantity.Value != ncr.AffectedQuantity)
        {
            ncr.AffectedQuantity = req.AffectedQuantity.Value;
            changedFields.Add("affectedQuantity");
        }
        if (req.DefectiveQuantity.HasValue && req.DefectiveQuantity != ncr.DefectiveQuantity)
        {
            ncr.DefectiveQuantity = req.DefectiveQuantity.Value;
            changedFields.Add("defectiveQuantity");
        }
        if (req.ContainmentActions != null && req.ContainmentActions != ncr.ContainmentActions)
        {
            ncr.ContainmentActions = req.ContainmentActions;
            changedFields.Add("containmentActions");
            if (!string.IsNullOrWhiteSpace(req.ContainmentActions))
            {
                ncr.ContainmentById = int.Parse(httpContextAccessor.HttpContext!.User.FindFirstValue(ClaimTypes.NameIdentifier)!);
                ncr.ContainmentAt = clock.UtcNow;
            }
        }
        if (req.MaterialCost.HasValue && req.MaterialCost != ncr.MaterialCost)
        {
            ncr.MaterialCost = req.MaterialCost.Value;
            changedFields.Add("materialCost");
        }
        if (req.LaborCost.HasValue && req.LaborCost != ncr.LaborCost)
        {
            ncr.LaborCost = req.LaborCost.Value;
            changedFields.Add("laborCost");
        }

        if (req.MaterialCost.HasValue || req.LaborCost.HasValue)
            ncr.TotalCostImpact = (ncr.MaterialCost ?? 0) + (ncr.LaborCost ?? 0);

        if (changedFields.Count == 0)
            return;

        db.LogActivityAt(
            "updated",
            $"Updated {changedFields.Count} field{(changedFields.Count == 1 ? "" : "s")}: {string.Join(", ", changedFields)}",
            ("NonConformance", ncr.Id));

        await db.SaveChangesAsync(cancellationToken);
    }
}
