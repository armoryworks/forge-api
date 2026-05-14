using System.Text.Json;

using FluentValidation;
using MediatR;
using Forge.Core.Entities;
using Forge.Core.Interfaces;
using Forge.Core.Models;
using Forge.Data.Context;
using Forge.Data.Extensions;

namespace Forge.Api.Features.Parts;

public record CreatePartCommand(
    string Name,
    string? Description,
    string? Revision,
    ProcurementSource ProcurementSource,
    InventoryClass InventoryClass,
    int? MaterialSpecId) : IRequest<PartDetailResponseModel>;

public class CreatePartCommandValidator : AbstractValidator<CreatePartCommand>
{
    public CreatePartCommandValidator()
    {
        RuleFor(x => x.Name).NotEmpty().MaximumLength(256);
        RuleFor(x => x.Description).MaximumLength(2000).When(x => x.Description is not null);
        RuleFor(x => x.Revision).MaximumLength(10).When(x => x.Revision is not null);
    }
}

public class CreatePartHandler(
    IPartRepository repo,
    ISyncQueueRepository syncQueue,
    IAccountingProviderFactory providerFactory,
    IBarcodeService barcodeService,
    AppDbContext db,
    ILogger<CreatePartHandler> logger) : IRequestHandler<CreatePartCommand, PartDetailResponseModel>
{
    public async Task<PartDetailResponseModel> Handle(CreatePartCommand request, CancellationToken cancellationToken)
    {
        var partNumber = await repo.GetNextPartNumberAsync(request.InventoryClass, cancellationToken);

        var part = new Part
        {
            PartNumber = partNumber,
            Name = request.Name.Trim(),
            Description = string.IsNullOrWhiteSpace(request.Description) ? null : request.Description.Trim(),
            Revision = request.Revision?.Trim() ?? "A",
            ProcurementSource = request.ProcurementSource,
            InventoryClass = request.InventoryClass,
            MaterialSpecId = request.MaterialSpecId,
            Status = PartStatus.Draft,
        };

        await repo.AddAsync(part, cancellationToken);

        db.LogActivityAt(
            "created",
            $"Created part: {part.PartNumber} — {part.Name} ({part.ProcurementSource} / {part.InventoryClass})",
            ("Part", part.Id));
        await db.SaveChangesAsync(cancellationToken);

        await barcodeService.CreateBarcodeAsync(
            BarcodeEntityType.Part, part.Id, part.PartNumber, cancellationToken);

        // Enqueue QB Item creation if accounting is connected
        try
        {
            var accountingService = await providerFactory.GetActiveProviderAsync(cancellationToken);
            if (accountingService is not null)
            {
                var syncStatus = await accountingService.GetSyncStatusAsync(cancellationToken);
                if (syncStatus.Connected)
                {
                    var item = new AccountingItem(
                        null, part.PartNumber, part.Name,
                        "NonInventory", null, null, part.PartNumber, true);
                    var payload = JsonSerializer.Serialize(item);
                    await syncQueue.EnqueueAsync("Part", part.Id, "CreateItem", payload, cancellationToken);
                    logger.LogInformation("Enqueued CreateItem sync for Part {PartId}", part.Id);
                }
            }
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to enqueue item sync for Part {PartId} — continuing", part.Id);
        }

        return (await repo.GetDetailAsync(part.Id, cancellationToken))!;
    }
}
