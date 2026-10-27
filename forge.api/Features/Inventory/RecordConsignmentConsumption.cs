using FluentValidation;
using MediatR;

using Forge.Api.Features.Jobs;
using Forge.Core.Enums;
using Forge.Core.Models;
using Forge.Data.Context;

namespace Forge.Api.Features.Inventory;

public record RecordConsignmentConsumptionCommand(int AgreementId, RecordConsignmentTransactionRequestModel Request, int UserId = 0) : IRequest<ConsignmentTransactionResponseModel>;

public class RecordConsignmentConsumptionValidator : AbstractValidator<RecordConsignmentConsumptionCommand>
{
    public RecordConsignmentConsumptionValidator()
    {
        RuleFor(x => x.Request.Quantity).GreaterThan(0m);
        RuleFor(x => x.Request.Notes).MaximumLength(2000);
    }
}

public class RecordConsignmentConsumptionHandler(AppDbContext db, IConsignmentStockService consignment, IMediator mediator)
    : IRequestHandler<RecordConsignmentConsumptionCommand, ConsignmentTransactionResponseModel>
{
    public async Task<ConsignmentTransactionResponseModel> Handle(RecordConsignmentConsumptionCommand command, CancellationToken cancellationToken)
    {
        var request = command.Request;
        await using var tx = db.Database.CurrentTransaction is null ? await db.Database.BeginTransactionAsync(cancellationToken) : null;

        var (transaction, owned) = await consignment.TakeOwnershipAsync(
            command.AgreementId, request.Quantity, request.BinContentId, request.StorageLocationId, request.JobId,
            request.Notes, command.UserId, cancellationToken);

        if (request.JobId is int jobId)
        {
            var taken = request.Quantity;
            foreach (var row in owned.DistinctBy(r => r.Id))
            {
                if (taken <= 0m)
                    break;
                var issue = Math.Min(taken, row.Quantity - row.ReservedQuantity);
                if (issue <= 0m)
                    continue;
                await mediator.Send(new CreateMaterialIssueCommand(
                    jobId, row.EntityId, null, issue, row.Id, row.LocationId, row.LotNumber, MaterialIssueType.Issue,
                    request.Notes ?? $"Consigned stock (CA-{command.AgreementId})", command.UserId), cancellationToken);
                taken -= issue;
            }
        }

        if (tx is not null)
            await tx.CommitAsync(cancellationToken);

        var models = await consignment.GetTransactionsAsync(command.AgreementId, cancellationToken);
        var model = models.First(m => m.Id == transaction.Id);
        var created = models.Where(m => m.Type == ConsignmentTransactionType.Consumption && m.Id >= transaction.Id).ToList();
        return model with
        {
            Quantity = created.Sum(m => m.Quantity),
            ExtendedAmount = created.Sum(m => m.ExtendedAmount),
        };
    }
}
