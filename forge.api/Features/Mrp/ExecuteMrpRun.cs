using MediatR;

using Forge.Core.Enums;
using Forge.Core.Interfaces;
using Forge.Core.Models;

namespace Forge.Api.Features.Mrp;

public record ExecuteMrpRunCommand(
    MrpRunType RunType = MrpRunType.Full,
    int PlanningHorizonDays = 90,
    List<int>? PartIds = null,
    int? InitiatedByUserId = null
) : IRequest<MrpRunResponseModel>;

public class ExecuteMrpRunHandler(IMrpService mrpService)
    : IRequestHandler<ExecuteMrpRunCommand, MrpRunResponseModel>
{
    public async Task<MrpRunResponseModel> Handle(ExecuteMrpRunCommand request, CancellationToken cancellationToken)
    {
        var options = new MrpRunOptions(
            RunType: request.RunType,
            PlanningHorizonDays: request.PlanningHorizonDays,
            PartIds: request.PartIds,
            IsSimulation: false,
            InitiatedByUserId: request.InitiatedByUserId
        );

        return await mrpService.ExecuteRunAsync(options, cancellationToken);
    }
}
