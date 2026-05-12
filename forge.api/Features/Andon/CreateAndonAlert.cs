using System.Security.Claims;

using FluentValidation;
using MediatR;
using Microsoft.AspNetCore.Http;

using Forge.Core.Entities;
using Forge.Core.Interfaces;
using Forge.Core.Models;
using Forge.Data.Context;

namespace Forge.Api.Features.Andon;

public record CreateAndonAlertCommand(CreateAndonAlertRequestModel Model) : IRequest<AndonAlertResponseModel>;

public class CreateAndonAlertValidator : AbstractValidator<CreateAndonAlertCommand>
{
    public CreateAndonAlertValidator()
    {
        RuleFor(x => x.Model.WorkCenterId).GreaterThan(0);
        RuleFor(x => x.Model.Type).IsInEnum();
    }
}

public class CreateAndonAlertHandler(AppDbContext db, IClock clock, IHttpContextAccessor httpContextAccessor, IMediator mediator)
    : IRequestHandler<CreateAndonAlertCommand, AndonAlertResponseModel>
{
    public async Task<AndonAlertResponseModel> Handle(
        CreateAndonAlertCommand request, CancellationToken cancellationToken)
    {
        var userId = int.Parse(httpContextAccessor.HttpContext!.User.FindFirstValue(ClaimTypes.NameIdentifier)!);

        var alert = new AndonAlert
        {
            WorkCenterId = request.Model.WorkCenterId,
            Type = request.Model.Type,
            RequestedById = userId,
            RequestedAt = clock.UtcNow,
            Notes = request.Model.Notes,
            JobId = request.Model.JobId,
        };

        db.AndonAlerts.Add(alert);
        await db.SaveChangesAsync(cancellationToken);

        var result = await mediator.Send(new GetAndonAlertsQuery(null, null), cancellationToken);
        return result.First(a => a.Id == alert.Id);
    }
}
