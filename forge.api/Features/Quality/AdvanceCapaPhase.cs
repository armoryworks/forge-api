using MediatR;

using Forge.Core.Interfaces;
using Forge.Core.Models;

namespace Forge.Api.Features.Quality;

public record AdvanceCapaPhaseCommand(int CapaId) : IRequest<CapaResponseModel>;

public class AdvanceCapaPhaseHandler(INcrCapaService ncrCapaService, MediatR.IMediator mediator)
    : IRequestHandler<AdvanceCapaPhaseCommand, CapaResponseModel>
{
    public async Task<CapaResponseModel> Handle(
        AdvanceCapaPhaseCommand command, CancellationToken cancellationToken)
    {
        await ncrCapaService.AdvanceCapaPhaseAsync(command.CapaId, cancellationToken);

        // Re-fetch full detail via existing handler
        return await mediator.Send(new GetCapaByIdQuery(command.CapaId), cancellationToken);
    }
}
