using FluentValidation;
using MediatR;

using Forge.Core.Entities;
using Forge.Core.Models;
using Forge.Data.Context;

namespace Forge.Api.Features.IoT;

public record CreateMachineConnectionCommand(CreateMachineConnectionRequestModel Model) : IRequest<MachineConnectionResponseModel>;

public class CreateMachineConnectionValidator : AbstractValidator<CreateMachineConnectionCommand>
{
    public CreateMachineConnectionValidator()
    {
        RuleFor(x => x.Model.Name).NotEmpty().MaximumLength(200);
        RuleFor(x => x.Model.OpcUaEndpoint).NotEmpty().MaximumLength(500);
        RuleFor(x => x.Model.WorkCenterId).GreaterThan(0);
        RuleFor(x => x.Model.PollIntervalMs).GreaterThanOrEqualTo(100);
    }
}

public class CreateMachineConnectionHandler(AppDbContext db, IMediator mediator)
    : IRequestHandler<CreateMachineConnectionCommand, MachineConnectionResponseModel>
{
    public async Task<MachineConnectionResponseModel> Handle(
        CreateMachineConnectionCommand request, CancellationToken cancellationToken)
    {
        var model = request.Model;
        var connection = new MachineConnection
        {
            WorkCenterId = model.WorkCenterId,
            Name = model.Name,
            OpcUaEndpoint = model.OpcUaEndpoint,
            SecurityPolicy = model.SecurityPolicy,
            AuthType = model.AuthType,
            EncryptedCredentials = model.Credentials,
            PollIntervalMs = model.PollIntervalMs,
        };

        foreach (var tagModel in model.Tags)
        {
            connection.Tags.Add(new MachineTag
            {
                TagName = tagModel.TagName,
                OpcNodeId = tagModel.OpcNodeId,
                DataType = tagModel.DataType,
                Unit = tagModel.Unit,
                WarningThresholdLow = tagModel.WarningThresholdLow,
                WarningThresholdHigh = tagModel.WarningThresholdHigh,
                AlarmThresholdLow = tagModel.AlarmThresholdLow,
                AlarmThresholdHigh = tagModel.AlarmThresholdHigh,
            });
        }

        db.MachineConnections.Add(connection);
        await db.SaveChangesAsync(cancellationToken);

        var result = await mediator.Send(new GetMachineConnectionsQuery(null), cancellationToken);
        return result.First(c => c.Id == connection.Id);
    }
}
