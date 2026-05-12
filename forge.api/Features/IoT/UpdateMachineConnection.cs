using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;

using Forge.Core.Entities;
using Forge.Core.Models;
using Forge.Data.Context;

namespace Forge.Api.Features.IoT;

public record UpdateMachineConnectionCommand(int Id, UpdateMachineConnectionRequestModel Model) : IRequest<MachineConnectionResponseModel>;

public class UpdateMachineConnectionValidator : AbstractValidator<UpdateMachineConnectionCommand>
{
    public UpdateMachineConnectionValidator()
    {
        RuleFor(x => x.Model.Name).NotEmpty().MaximumLength(200);
        RuleFor(x => x.Model.OpcUaEndpoint).NotEmpty().MaximumLength(500);
        RuleFor(x => x.Model.PollIntervalMs).GreaterThanOrEqualTo(100);
    }
}

public class UpdateMachineConnectionHandler(AppDbContext db, IMediator mediator)
    : IRequestHandler<UpdateMachineConnectionCommand, MachineConnectionResponseModel>
{
    public async Task<MachineConnectionResponseModel> Handle(
        UpdateMachineConnectionCommand request, CancellationToken cancellationToken)
    {
        var connection = await db.MachineConnections
            .Include(c => c.Tags)
            .FirstOrDefaultAsync(c => c.Id == request.Id, cancellationToken)
            ?? throw new KeyNotFoundException($"MachineConnection {request.Id} not found");

        var model = request.Model;
        connection.Name = model.Name;
        connection.OpcUaEndpoint = model.OpcUaEndpoint;
        connection.SecurityPolicy = model.SecurityPolicy;
        connection.AuthType = model.AuthType;
        connection.PollIntervalMs = model.PollIntervalMs;
        connection.IsActive = model.IsActive;

        if (!string.IsNullOrEmpty(model.Credentials))
            connection.EncryptedCredentials = model.Credentials;

        // Replace tags
        db.MachineTags.RemoveRange(connection.Tags);
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

        await db.SaveChangesAsync(cancellationToken);

        var result = await mediator.Send(new GetMachineConnectionsQuery(null), cancellationToken);
        return result.First(c => c.Id == connection.Id);
    }
}
