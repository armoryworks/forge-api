using System.Security.Claims;

using MediatR;

using Forge.Core.Interfaces;
using Forge.Core.Models;

namespace Forge.Api.Features.KanbanReplenishment;

public record TriggerKanbanReplenishmentCommand(int Id, TriggerKanbanReplenishmentRequestModel Request, int? UserId) : IRequest;

public class TriggerKanbanReplenishmentHandler(IKanbanReplenishmentService kanbanService) : IRequestHandler<TriggerKanbanReplenishmentCommand>
{
    public async Task Handle(TriggerKanbanReplenishmentCommand command, CancellationToken cancellationToken)
    {
        var triggerType = Enum.TryParse<KanbanTriggerType>(command.Request.TriggerType, true, out var parsed)
            ? parsed
            : KanbanTriggerType.Manual;

        await kanbanService.TriggerReplenishmentAsync(command.Id, triggerType, command.UserId, cancellationToken);
    }
}
