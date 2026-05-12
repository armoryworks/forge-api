using System.Security.Claims;

using FluentValidation;
using MediatR;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;

using Forge.Core.Entities;
using Forge.Core.Interfaces;
using Forge.Core.Models;
using Forge.Data.Context;

namespace Forge.Api.Features.Eco;

public record CreateEcoCommand(CreateEcoRequestModel Request) : IRequest<EcoResponseModel>;

public class CreateEcoValidator : AbstractValidator<CreateEcoCommand>
{
    public CreateEcoValidator()
    {
        RuleFor(x => x.Request.Title).NotEmpty().MaximumLength(200);
        RuleFor(x => x.Request.Description).NotEmpty().MaximumLength(2000);
        RuleFor(x => x.Request.ReasonForChange).MaximumLength(2000);
        RuleFor(x => x.Request.ImpactAnalysis).MaximumLength(2000);
    }
}

public class CreateEcoHandler(AppDbContext db, IHttpContextAccessor httpContext, IClock clock)
    : IRequestHandler<CreateEcoCommand, EcoResponseModel>
{
    public async Task<EcoResponseModel> Handle(CreateEcoCommand request, CancellationToken cancellationToken)
    {
        var userId = int.Parse(httpContext.HttpContext!.User.FindFirstValue(ClaimTypes.NameIdentifier)!);

        // Generate ECO number
        var today = DateOnly.FromDateTime(clock.UtcNow.UtcDateTime);
        var prefix = $"ECO-{today:yyyyMMdd}-";
        var lastNumber = await db.EngineeringChangeOrders
            .AsNoTracking()
            .Where(e => e.EcoNumber.StartsWith(prefix))
            .OrderByDescending(e => e.EcoNumber)
            .Select(e => e.EcoNumber)
            .FirstOrDefaultAsync(cancellationToken);

        var seq = 1;
        if (lastNumber is not null)
        {
            var parts = lastNumber.Split('-');
            if (parts.Length == 3 && int.TryParse(parts[2], out var lastSeq))
                seq = lastSeq + 1;
        }

        var eco = new EngineeringChangeOrder
        {
            EcoNumber = $"{prefix}{seq:D3}",
            Title = request.Request.Title,
            Description = request.Request.Description,
            ChangeType = request.Request.ChangeType,
            Priority = request.Request.Priority,
            ReasonForChange = request.Request.ReasonForChange,
            ImpactAnalysis = request.Request.ImpactAnalysis,
            EffectiveDate = request.Request.EffectiveDate,
            RequestedById = userId,
        };

        db.EngineeringChangeOrders.Add(eco);
        await db.SaveChangesAsync(cancellationToken);

        var user = await db.Users.AsNoTracking()
            .FirstAsync(u => u.Id == userId, cancellationToken);

        return new EcoResponseModel
        {
            Id = eco.Id,
            EcoNumber = eco.EcoNumber,
            Title = eco.Title,
            Description = eco.Description,
            ChangeType = eco.ChangeType,
            Status = eco.Status,
            Priority = eco.Priority,
            ReasonForChange = eco.ReasonForChange,
            ImpactAnalysis = eco.ImpactAnalysis,
            EffectiveDate = eco.EffectiveDate,
            RequestedById = eco.RequestedById,
            RequestedByName = $"{user.LastName}, {user.FirstName}",
            CreatedAt = eco.CreatedAt,
        };
    }
}
