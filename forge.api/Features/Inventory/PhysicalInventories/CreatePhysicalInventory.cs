using FluentValidation;
using MediatR;

using Forge.Core.Models;

namespace Forge.Api.Features.Inventory.PhysicalInventories;

public record CreatePhysicalInventoryCommand(CreatePhysicalInventoryRequestModel Data, int UserId) : IRequest<PhysicalInventoryDetailResponseModel>;

public class CreatePhysicalInventoryValidator : AbstractValidator<CreatePhysicalInventoryCommand>
{
    public CreatePhysicalInventoryValidator()
    {
        RuleFor(x => x.Data.CountDate).NotEmpty();
        RuleFor(x => x.Data.Description).MaximumLength(200);
        RuleFor(x => x.Data.Notes).MaximumLength(1000);
        RuleFor(x => x.Data.RecountTolerancePercent).InclusiveBetween(0m, 100m);
        RuleFor(x => x.Data.ScopeLocationId).GreaterThan(0).When(x => x.Data.ScopeLocationId.HasValue);
    }
}

public class CreatePhysicalInventoryHandler(IPhysicalInventoryService service) : IRequestHandler<CreatePhysicalInventoryCommand, PhysicalInventoryDetailResponseModel>
{
    public Task<PhysicalInventoryDetailResponseModel> Handle(CreatePhysicalInventoryCommand request, CancellationToken cancellationToken)
        => service.CreateAsync(request.Data, request.UserId, cancellationToken);
}
