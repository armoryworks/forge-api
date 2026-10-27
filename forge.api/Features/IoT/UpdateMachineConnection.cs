using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;

using Forge.Core.Entities;
using Forge.Core.Interfaces;
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
        RuleFor(x => x.Model.WorkCenterId).GreaterThan(0).When(x => x.Model.WorkCenterId.HasValue);
        RuleFor(x => x.Model.Credentials).MaximumLength(1000);
        RuleForEach(x => x.Model.Tags).ChildRules(MachineTagRules.Apply);
        RuleFor(x => x.Model.Tags)
            .Must(MachineTagRules.HaveUniqueNodeIds).WithMessage("Each tag must map a different OPC node id.");
    }
}

public class UpdateMachineConnectionHandler(AppDbContext db, IMediator mediator, ITokenEncryptionService encryption)
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
        if (model.WorkCenterId is { } workCenterId)
            connection.WorkCenterId = workCenterId;
        connection.Name = model.Name;
        connection.OpcUaEndpoint = model.OpcUaEndpoint;
        connection.SecurityPolicy = model.SecurityPolicy;
        connection.AuthType = model.AuthType;
        connection.PollIntervalMs = model.PollIntervalMs;
        connection.IsActive = model.IsActive;

        if (!string.IsNullOrEmpty(model.Credentials))
            connection.EncryptedCredentials = encryption.Encrypt(model.Credentials);

        var incoming = model.Tags.ToDictionary(t => t.OpcNodeId.Trim(), StringComparer.Ordinal);
        var removed = connection.Tags.Where(t => !incoming.ContainsKey(t.OpcNodeId)).ToList();
        var removedIds = removed.Select(t => t.Id).ToList();
        var withHistory = await db.MachineDataPoints
            .Where(p => removedIds.Contains(p.TagId))
            .Select(p => p.TagId)
            .Distinct()
            .ToListAsync(cancellationToken);

        foreach (var tag in removed)
        {
            if (withHistory.Contains(tag.Id))
                tag.IsActive = false;
            else
                db.MachineTags.Remove(tag);
        }

        foreach (var (nodeId, tagModel) in incoming)
        {
            var tag = connection.Tags.FirstOrDefault(t => t.OpcNodeId == nodeId);
            if (tag is null)
            {
                tag = new MachineTag { OpcNodeId = nodeId };
                connection.Tags.Add(tag);
            }

            tag.TagName = tagModel.TagName;
            tag.DataType = tagModel.DataType;
            tag.Unit = tagModel.Unit;
            tag.WarningThresholdLow = tagModel.WarningThresholdLow;
            tag.WarningThresholdHigh = tagModel.WarningThresholdHigh;
            tag.AlarmThresholdLow = tagModel.AlarmThresholdLow;
            tag.AlarmThresholdHigh = tagModel.AlarmThresholdHigh;
            tag.IsActive = true;
        }

        await db.SaveChangesAsync(cancellationToken);

        var result = await mediator.Send(new GetMachineConnectionsQuery(null), cancellationToken);
        return result.First(c => c.Id == connection.Id);
    }
}
