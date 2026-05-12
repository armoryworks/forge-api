using MediatR;

using Forge.Core.Interfaces;
using Forge.Core.Models;

namespace Forge.Api.Features.KanbanReplenishment;

public record UpdateKanbanCardCommand(int Id, UpdateKanbanCardRequestModel Request) : IRequest<KanbanCardResponseModel>;

public class UpdateKanbanCardHandler(IKanbanReplenishmentService kanbanService) : IRequestHandler<UpdateKanbanCardCommand, KanbanCardResponseModel>
{
    public async Task<KanbanCardResponseModel> Handle(UpdateKanbanCardCommand command, CancellationToken cancellationToken)
    {
        var card = await kanbanService.UpdateCardAsync(command.Id, command.Request, cancellationToken);

        return new KanbanCardResponseModel
        {
            Id = card.Id,
            CardNumber = card.CardNumber,
            PartId = card.PartId,
            WorkCenterId = card.WorkCenterId,
            BinQuantity = card.BinQuantity,
            NumberOfBins = card.NumberOfBins,
            Status = card.Status.ToString(),
            SupplySource = card.SupplySource.ToString(),
            IsActive = card.IsActive,
        };
    }
}
