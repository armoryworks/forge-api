using System.Globalization;

using FluentValidation;
using MediatR;

using Forge.Api.Validation;
using Forge.Core.Entities;
using Forge.Core.Enums;
using Forge.Core.Interfaces;
using Forge.Core.Models;
using Forge.Data.Context;
using Forge.Data.Extensions;

namespace Forge.Api.Features.PurchaseOrders;

public record AddPurchaseOrderLineCommand(int PurchaseOrderId, AddPurchaseOrderLineRequestModel Data)
    : IRequest<PurchaseOrderDetailResponseModel>;

public class AddPurchaseOrderLineValidator : AbstractValidator<AddPurchaseOrderLineCommand>
{
    public AddPurchaseOrderLineValidator()
    {
        RuleFor(x => x.PurchaseOrderId).GreaterThan(0);
        RuleFor(x => x.Data.PartId).GreaterThan(0).When(x => x.Data.PartId.HasValue);
        RuleFor(x => x.Data.Description)
            .NotEmpty()
            .When(x => x.Data.PartId is null)
            .WithMessage("Description is required when the line has no part.");
        RuleFor(x => x.Data.Description).MaximumLength(500);
        RuleFor(x => x.Data.Notes).MaximumLength(1000);
        RuleFor(x => x.Data.Quantity).GreaterThan(0m);
        RuleFor(x => x.Data.UnitPrice).GreaterThanOrEqualTo(0m);
    }
}

public class AddPurchaseOrderLineHandler(
    IPurchaseOrderRepository repo,
    IPartRepository partRepo,
    AppDbContext db,
    IMediator mediator)
    : IRequestHandler<AddPurchaseOrderLineCommand, PurchaseOrderDetailResponseModel>
{
    public async Task<PurchaseOrderDetailResponseModel> Handle(AddPurchaseOrderLineCommand request, CancellationToken cancellationToken)
    {
        var po = await repo.FindWithDetailsAsync(request.PurchaseOrderId, cancellationToken)
            ?? throw new KeyNotFoundException($"Purchase order {request.PurchaseOrderId} not found");

        if (po.Status != PurchaseOrderStatus.Draft)
            throw new InvalidOperationException("Lines can only be added to draft purchase orders.");

        var data = request.Data;
        Part? part = null;
        if (data.PartId is int partId)
        {
            part = await partRepo.FindAsync(partId, cancellationToken);
            ActiveCheck.EnsureActive(part, "Part", "partId", partId);
        }

        var description = string.IsNullOrWhiteSpace(data.Description)
            ? part?.Description ?? part?.Name ?? string.Empty
            : data.Description.Trim();

        po.Lines.Add(new PurchaseOrderLine
        {
            PartId = data.PartId,
            Description = description,
            OrderedQuantity = data.Quantity,
            UnitPrice = data.UnitPrice,
            Notes = data.Notes,
            PurchaseUnitId = data.PurchaseUnitId,
            ManualOverrideReason = data.ManualOverrideReason,
        });

        db.LogActivityAt(
            "line-added",
            $"Added line {Label(part, description)}: {data.Quantity.ToString("0.####", CultureInfo.InvariantCulture)} @ {data.UnitPrice.ToString("0.00##", CultureInfo.InvariantCulture)}",
            ("PurchaseOrder", po.Id));

        await repo.SaveChangesAsync(cancellationToken);

        return await mediator.Send(new GetPurchaseOrderByIdQuery(po.Id), cancellationToken);
    }

    private static string Label(Part? part, string description) =>
        string.IsNullOrWhiteSpace(part?.PartNumber) ? description : part.PartNumber;
}
