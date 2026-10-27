using FluentValidation;
using MediatR;

using Forge.Core.Models;

namespace Forge.Api.Features.Inventory.PhysicalInventories;

public record EnterPhysicalCountCommand(int Id, int TagId, EnterPhysicalCountRequestModel Data, int UserId) : IRequest<PhysicalInventoryTagResponseModel>;

public class EnterPhysicalCountValidator : AbstractValidator<EnterPhysicalCountCommand>
{
    public EnterPhysicalCountValidator()
    {
        RuleFor(x => x.Data.Quantity).GreaterThanOrEqualTo(0m).WithMessage("A counted quantity cannot be negative.");
        RuleFor(x => x.Data.Notes).MaximumLength(500);
    }
}

public class EnterPhysicalCountHandler(IPhysicalInventoryService service) : IRequestHandler<EnterPhysicalCountCommand, PhysicalInventoryTagResponseModel>
{
    public Task<PhysicalInventoryTagResponseModel> Handle(EnterPhysicalCountCommand request, CancellationToken cancellationToken)
        => service.EnterCountAsync(request.Id, request.TagId, request.Data, request.UserId, cancellationToken);
}
