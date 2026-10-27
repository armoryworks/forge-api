using FluentValidation;
using MediatR;

using Forge.Core.Models;

namespace Forge.Api.Features.Inventory.PhysicalInventories;

public record AddPhysicalInventoryTagCommand(int Id, AddPhysicalInventoryTagRequestModel Data, int UserId) : IRequest<PhysicalInventoryTagResponseModel>;

public class AddPhysicalInventoryTagValidator : AbstractValidator<AddPhysicalInventoryTagCommand>
{
    public AddPhysicalInventoryTagValidator()
    {
        RuleFor(x => x.Data.LocationId).GreaterThan(0);
        RuleFor(x => x.Data.PartId).GreaterThan(0);
        RuleFor(x => x.Data.LotNumber).MaximumLength(100);
        RuleFor(x => x.Data.SerialNumber).MaximumLength(100);
        RuleFor(x => x.Data.Notes).MaximumLength(500);
        RuleFor(x => x.Data.Quantity).GreaterThanOrEqualTo(0m).When(x => x.Data.Quantity.HasValue)
            .WithMessage("A counted quantity cannot be negative.");
    }
}

public class AddPhysicalInventoryTagHandler(IPhysicalInventoryService service) : IRequestHandler<AddPhysicalInventoryTagCommand, PhysicalInventoryTagResponseModel>
{
    public Task<PhysicalInventoryTagResponseModel> Handle(AddPhysicalInventoryTagCommand request, CancellationToken cancellationToken)
        => service.AddTagAsync(request.Id, request.Data, request.UserId, cancellationToken);
}
