using System.Text.Json;

using FluentValidation;
using MediatR;
using Forge.Core.Entities;
using Forge.Core.Enums;
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
    int? MaterialSpecId,
    // Optional caller-supplied part number — see CreatePartRequestModel.PartNumber.
    string? PartNumber = null) : IRequest<PartDetailResponseModel>;

public class CreatePartCommandValidator : AbstractValidator<CreatePartCommand>
{
    public CreatePartCommandValidator()
    {
        RuleFor(x => x.Name).NotEmpty().MaximumLength(256);
        RuleFor(x => x.Description).MaximumLength(2000).When(x => x.Description is not null);
        RuleFor(x => x.Revision).MaximumLength(10).When(x => x.Revision is not null);
        // Matches the parts.part_number column (varchar(50)). Uniqueness is
        // checked in the handler since it needs a DB lookup.
        RuleFor(x => x.PartNumber).MaximumLength(50).When(x => !string.IsNullOrWhiteSpace(x.PartNumber));
    }
}

public class CreatePartHandler(
    IPartRepository repo,
    ISystemSettingRepository systemSettings,
    ISyncQueueRepository syncQueue,
    IAccountingProviderFactory providerFactory,
    IBarcodeService barcodeService,
    IBusinessIdentifierService identifiers,
    AppDbContext db,
    ILogger<CreatePartHandler> logger) : IRequestHandler<CreatePartCommand, PartDetailResponseModel>
{
    // System setting that gates caller-supplied part numbers. Stored as "true"/"false".
    private const string AllowManualPartNumbersKey = "parts.allow_manual_numbers";

    public async Task<PartDetailResponseModel> Handle(CreatePartCommand request, CancellationToken cancellationToken)
    {
        var partNumber = await ResolvePartNumberAsync(request, cancellationToken);

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

        // Record the number in the identifier registry (history + resolution).
        await identifiers.IssueAsync(BusinessEntityType.Part, part.Id, part.PartNumber, cancellationToken);

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

    // Uses a caller-supplied part number when manual numbers are enabled and one
    // was provided; otherwise auto-generates the next sequential number.
    private async Task<string> ResolvePartNumberAsync(CreatePartCommand request, CancellationToken ct)
    {
        var supplied = request.PartNumber?.Trim();
        if (!string.IsNullOrWhiteSpace(supplied) && await ManualPartNumbersAllowedAsync(ct))
        {
            if (await repo.PartNumberExistsAsync(supplied, null, ct))
                throw new InvalidOperationException($"Part number '{supplied}' is already in use.");
            return supplied;
        }

        return await repo.GetNextPartNumberAsync(request.InventoryClass, ct);
    }

    private async Task<bool> ManualPartNumbersAllowedAsync(CancellationToken ct)
    {
        var setting = await systemSettings.FindByKeyAsync(AllowManualPartNumbersKey, ct);
        return setting is not null && bool.TryParse(setting.Value, out var enabled) && enabled;
    }
}
