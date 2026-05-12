using System.Security.Claims;

using FluentValidation;
using MediatR;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;

using Forge.Core.Entities;
using Forge.Core.Interfaces;
using Forge.Core.Models;
using Forge.Data.Context;

namespace Forge.Api.Features.Parts;

public record CreatePartAlternateCommand(int PartId, CreatePartAlternateRequestModel Request) : IRequest<PartAlternateResponseModel>;

public class CreatePartAlternateValidator : AbstractValidator<CreatePartAlternateCommand>
{
    public CreatePartAlternateValidator()
    {
        RuleFor(x => x.Request.AlternatePartId).GreaterThan(0);
        RuleFor(x => x.Request.Priority).GreaterThan(0);
        RuleFor(x => x.Request.Notes).MaximumLength(500);
    }
}

public class CreatePartAlternateHandler(AppDbContext db, IHttpContextAccessor httpContext, IClock clock)
    : IRequestHandler<CreatePartAlternateCommand, PartAlternateResponseModel>
{
    public async Task<PartAlternateResponseModel> Handle(CreatePartAlternateCommand request, CancellationToken cancellationToken)
    {
        var part = await db.Parts.AsNoTracking().FirstOrDefaultAsync(p => p.Id == request.PartId, cancellationToken)
            ?? throw new KeyNotFoundException($"Part {request.PartId} not found");

        var alternatePart = await db.Parts.AsNoTracking().FirstOrDefaultAsync(p => p.Id == request.Request.AlternatePartId, cancellationToken)
            ?? throw new KeyNotFoundException($"Alternate part {request.Request.AlternatePartId} not found");

        if (request.PartId == request.Request.AlternatePartId)
            throw new InvalidOperationException("A part cannot be its own alternate");

        // Check for existing relationship
        var exists = await db.PartAlternates.AnyAsync(
            a => (a.PartId == request.PartId && a.AlternatePartId == request.Request.AlternatePartId)
              || (a.PartId == request.Request.AlternatePartId && a.AlternatePartId == request.PartId),
            cancellationToken);

        if (exists)
            throw new InvalidOperationException("This alternate relationship already exists");

        var userId = int.Parse(httpContext.HttpContext!.User.FindFirstValue(ClaimTypes.NameIdentifier)!);

        var alternate = new PartAlternate
        {
            PartId = request.PartId,
            AlternatePartId = request.Request.AlternatePartId,
            Priority = request.Request.Priority,
            Type = request.Request.Type,
            ConversionFactor = request.Request.ConversionFactor,
            IsApproved = request.Request.IsApproved,
            ApprovedById = request.Request.IsApproved ? userId : null,
            ApprovedAt = request.Request.IsApproved ? clock.UtcNow : null,
            Notes = request.Request.Notes,
            IsBidirectional = request.Request.IsBidirectional,
        };

        db.PartAlternates.Add(alternate);
        await db.SaveChangesAsync(cancellationToken);

        return new PartAlternateResponseModel
        {
            Id = alternate.Id,
            PartId = alternate.PartId,
            AlternatePartId = alternate.AlternatePartId,
            AlternatePartNumber = alternatePart.PartNumber,
            AlternatePartName = alternatePart.Name,
            Priority = alternate.Priority,
            Type = alternate.Type,
            ConversionFactor = alternate.ConversionFactor,
            IsApproved = alternate.IsApproved,
            ApprovedAt = alternate.ApprovedAt,
            Notes = alternate.Notes,
            IsBidirectional = alternate.IsBidirectional,
            CreatedAt = alternate.CreatedAt,
        };
    }
}
