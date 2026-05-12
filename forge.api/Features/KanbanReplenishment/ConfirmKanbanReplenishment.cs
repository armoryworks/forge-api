using MediatR;

using Forge.Core.Interfaces;
using Forge.Core.Models;

namespace Forge.Api.Features.KanbanReplenishment;

public record ConfirmKanbanReplenishmentCommand(int Id, ConfirmKanbanReplenishmentRequestModel Request) : IRequest;

public class ConfirmKanbanReplenishmentHandler(IKanbanReplenishmentService kanbanService) : IRequestHandler<ConfirmKanbanReplenishmentCommand>
{
    public async Task Handle(ConfirmKanbanReplenishmentCommand command, CancellationToken cancellationToken)
    {
        await kanbanService.ConfirmReplenishmentAsync(command.Id, command.Request.FulfilledQuantity, cancellationToken);
    }
}
