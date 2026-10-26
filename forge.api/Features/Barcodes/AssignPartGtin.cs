using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;

using Npgsql;

using Forge.Core.Interfaces;
using Forge.Data.Context;
using Forge.Data.Extensions;

namespace Forge.Api.Features.Barcodes;

/// <summary>
/// Give a part a licensed GS1 GTIN — either paste a purchased GTIN (validated), or auto-allocate the next
/// one from the configured company prefix. The part's scannable barcode is re-synced to the GTIN. Without
/// this, a part keeps its free internal code. Gated by CAP-MD-GS1.
/// </summary>
public record AssignPartGtinCommand(int PartId, string? ManualGtin) : IRequest<AssignPartGtinResponseModel>;

public record AssignPartGtinResponseModel(int PartId, string Gtin, string Source);

public class AssignPartGtinValidator : AbstractValidator<AssignPartGtinCommand>
{
    public AssignPartGtinValidator()
    {
        RuleFor(x => x.ManualGtin)
            .Must(g => string.IsNullOrWhiteSpace(g) || Gs1.IsValidGtin(g.Trim()))
            .WithMessage("That isn't a valid GTIN — check the length (8/12/13/14 digits) and check digit.");
    }
}

public class AssignPartGtinHandler(AppDbContext db, ISystemSettingRepository settings, IBarcodeService barcodes)
    : IRequestHandler<AssignPartGtinCommand, AssignPartGtinResponseModel>
{
    private const string GtinIndex = "ux_parts_gtin";
    private const int AllocationAttempts = 3;

    public async Task<AssignPartGtinResponseModel> Handle(AssignPartGtinCommand request, CancellationToken cancellationToken)
    {
        var allocating = string.IsNullOrWhiteSpace(request.ManualGtin);
        for (var attempt = 1; ; attempt++)
        {
            try
            {
                var result = await AssignAsync(request, cancellationToken);
                await barcodes.RefreshPartBarcodeAsync(result.PartId, cancellationToken);
                return result;
            }
            catch (DbUpdateException ex) when (allocating && attempt < AllocationAttempts
                && ex.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation, ConstraintName: GtinIndex })
            {
                db.ChangeTracker.Clear();
            }
        }
    }

    private async Task<AssignPartGtinResponseModel> AssignAsync(AssignPartGtinCommand request, CancellationToken cancellationToken)
    {
        await using var tx = db.Database.CurrentTransaction is null
            ? await db.Database.BeginTransactionAsync(cancellationToken)
            : null;
        await db.AcquireAdvisoryLockAsync("parts.gtin", string.Empty, cancellationToken);

        var part = await db.Parts.FirstOrDefaultAsync(p => p.Id == request.PartId, cancellationToken)
            ?? throw new KeyNotFoundException($"Part {request.PartId} not found.");

        string gtin;
        string source;

        if (!string.IsNullOrWhiteSpace(request.ManualGtin))
        {
            gtin = request.ManualGtin.Trim();
            if (await db.Parts.AnyAsync(p => p.Gtin == gtin && p.Id != part.Id, cancellationToken))
                throw new InvalidOperationException($"GTIN {gtin} is already assigned to another part.");
            source = "Manual";
        }
        else
        {
            var prefix = (await settings.FindByKeyAsync(Gs1.CompanyPrefixKey, cancellationToken))?.Value?.Trim();
            if (string.IsNullOrWhiteSpace(prefix))
                throw new InvalidOperationException(
                    "No GS1 company prefix is configured. Enter a purchased GTIN manually, or set your prefix in Admin → GS1.");

            var nextRef = long.TryParse((await settings.FindByKeyAsync(Gs1.NextItemRefKey, cancellationToken))?.Value, out var n) ? n : 1;

            // Allocate the next reference, skipping any already-used GTIN (defensive against gaps/manual entries).
            string candidate;
            var attempts = 0;
            do
            {
                candidate = Gs1.BuildGtin13(prefix, nextRef);
                nextRef++;
                attempts++;
            }
            while (await db.Parts.AnyAsync(p => p.Gtin == candidate, cancellationToken) && attempts < 1000);

            gtin = candidate;
            await settings.UpsertAsync(Gs1.NextItemRefKey, nextRef.ToString(System.Globalization.CultureInfo.InvariantCulture),
                "Next GS1 item reference to allocate", cancellationToken);
            source = "Allocated";
        }

        part.Gtin = gtin;
        db.LogActivityAt("part-gtin-assigned", $"GS1 GTIN {gtin} assigned ({source})", ("Part", part.Id));
        await db.SaveChangesAsync(cancellationToken);

        if (tx is not null)
            await tx.CommitAsync(cancellationToken);

        return new AssignPartGtinResponseModel(part.Id, gtin, source);
    }
}
