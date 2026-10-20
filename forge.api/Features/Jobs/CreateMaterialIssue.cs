using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;

using Forge.Api.Features.Accounting;
using Forge.Api.Features.Inventory;
using Forge.Api.Features.Quality;
using Forge.Api.Features.Scanner;
using Forge.Core.Entities;
using Forge.Core.Enums;
using Forge.Core.Interfaces;
using Forge.Core.Models;
using Forge.Data.Context;

namespace Forge.Api.Features.Jobs;

public record CreateMaterialIssueCommand(
    int JobId,
    int PartId,
    int? OperationId,
    decimal Quantity,
    int? BinContentId,
    int? StorageLocationId,
    string? LotNumber,
    MaterialIssueType IssueType,
    string? Notes,
    int IssuedById,
    bool StockDrawnByCaller = false,
    bool AllowExpiredLot = false) : IRequest<MaterialIssueResponseModel>;

public class CreateMaterialIssueValidator : AbstractValidator<CreateMaterialIssueCommand>
{
    public CreateMaterialIssueValidator()
    {
        RuleFor(x => x.PartId).GreaterThan(0);
        RuleFor(x => x.Quantity).GreaterThan(0);
    }
}

public class CreateMaterialIssueHandler(
    AppDbContext db,
    // Phase-2 STAGE E — optional / null-default so the handler stays constructible without an accounting
    // context (isolated unit tests). Production DI supplies it; with CAP-ACCT-FULLGL off it no-ops.
    IMaterialIssuePostingService? posting = null,
    IClock? clock = null)
    : IRequestHandler<CreateMaterialIssueCommand, MaterialIssueResponseModel>
{
    public async Task<MaterialIssueResponseModel> Handle(
        CreateMaterialIssueCommand request, CancellationToken cancellationToken)
    {
        // INV-SF2 / F-033: source-state guard — archived jobs are closed to material transactions
        var job = await db.Jobs.AsNoTracking()
            .FirstOrDefaultAsync(j => j.Id == request.JobId, cancellationToken)
            ?? throw new KeyNotFoundException($"Job {request.JobId} not found");

        if (job.IsArchived)
            throw new InvalidOperationException(
                $"Cannot issue material to archived job {request.JobId}.");

        // Get unit cost from last PO line for this part, or zero
        var unitCost = await db.PurchaseOrderLines
            .AsNoTracking()
            .Where(pol => pol.PartId == request.PartId)
            .OrderByDescending(pol => pol.Id)
            .Select(pol => pol.UnitPrice)
            .FirstOrDefaultAsync(cancellationToken);

        var now = clock?.UtcNow ?? DateTimeOffset.UtcNow;
        var draws = request.StockDrawnByCaller
            ? [new StockDraw(request.LotNumber, request.Quantity, request.BinContentId, null)]
            : request.BinContentId is int binContentId
                ? await DrawFromBinAsync(request, binContentId, now, cancellationToken)
                : await DrawFromLocationAsync(request, now, cancellationToken);

        var issues = draws.Select(draw => new MaterialIssue
        {
            JobId = request.JobId,
            PartId = request.PartId,
            OperationId = request.OperationId,
            Quantity = draw.Quantity,
            UnitCost = unitCost,
            IssuedById = request.IssuedById,
            IssuedAt = now,
            BinContentId = draw.BinContentId,
            BinContent = draw.Row,
            StorageLocationId = request.StorageLocationId ?? draw.Row?.LocationId,
            LotNumber = draw.LotNumber,
            IssueType = request.IssueType,
            Notes = request.Notes,
        }).ToList();

        db.MaterialIssues.AddRange(issues);

        // One transaction: the issue + bin decrement AND the inline WIP / inventory posting commit (or roll
        // back) together — the locked inline model. The engine's SaveChanges joins this transaction; a posting
        // failure (FULLGL on) unwinds the issue too. On Npgsql this is a real transaction; the in-memory test
        // provider treats it as an ignored no-op. tx is opened only when posting is wired.
        await using var tx = posting is not null && db.Database.CurrentTransaction is null
            ? await db.Database.BeginTransactionAsync(cancellationToken)
            : null;

        await db.SaveChangesAsync(cancellationToken);

        // Inline WIP / inventory posting (Phase-2 STAGE E) — no-op while CAP-ACCT-FULLGL is off. Runs after the
        // issue is flushed so its id resolves; relieves the perpetual valuation store at weighted-average.
        if (posting is not null)
        {
            foreach (var posted in issues)
                await posting.PostMaterialIssueAsync(
                    posted.Id, DateOnly.FromDateTime(posted.IssuedAt.UtcDateTime), request.IssuedById, cancellationToken);
        }

        if (tx is not null)
            await tx.CommitAsync(cancellationToken);

        var part = await db.Parts.AsNoTracking()
            .FirstAsync(p => p.Id == request.PartId, cancellationToken);
        var issue = issues[0];

        return new MaterialIssueResponseModel
        {
            Id = issue.Id,
            JobId = issue.JobId,
            PartId = issue.PartId,
            PartNumber = part.PartNumber,
            PartDescription = part.Description ?? string.Empty,
            OperationId = issue.OperationId,
            Quantity = issues.Sum(i => i.Quantity),
            UnitCost = issue.UnitCost,
            TotalCost = issues.Sum(i => i.Quantity * i.UnitCost),
            IssuedAt = issue.IssuedAt,
            LotNumber = issues.Count == 1 ? issue.LotNumber : null,
            IssueType = issue.IssueType,
            Notes = issue.Notes,
        };
    }

    private async Task<List<StockDraw>> DrawFromBinAsync(
        CreateMaterialIssueCommand request, int binContentId, DateTimeOffset now, CancellationToken ct)
    {
        var bin = await db.BinContents.FindAsync([binContentId], ct)
            ?? throw new KeyNotFoundException($"BinContent {binContentId} not found");
        if (bin.EntityType != "part" || bin.EntityId != request.PartId)
            throw new InvalidOperationException($"BinContent {binContentId} does not hold part {request.PartId}.");
        if (request.IssueType == MaterialIssueType.Return && bin.RemovedAt is not null)
            return [await RestoreAsync(request, bin.LocationId, bin.LotNumber ?? request.LotNumber, now, ct)];

        if (request.IssueType == MaterialIssueType.Issue && bin.Status == BinContentStatus.QcHold)
            throw await LotQualityHold.RefusalAsync(db, request.PartId, bin.LotNumber, ct);
        if (request.IssueType == MaterialIssueType.Issue)
            await RefuseExpiredLotAsync(request, bin.LotNumber, now, ct);

        // Decrement bin quantity for Issue, increment for Return
        if (request.IssueType == MaterialIssueType.Issue || request.IssueType == MaterialIssueType.Scrap)
        {
            if (bin.Quantity < request.Quantity)
                throw new InvalidOperationException($"Insufficient quantity in bin. Available: {bin.Quantity}, Requested: {request.Quantity}");
            bin.Quantity -= request.Quantity;
        }
        else if (request.IssueType == MaterialIssueType.Return)
        {
            bin.Quantity += request.Quantity;
        }

        // Record bin movement
        db.BinMovements.Add(new BinMovement
        {
            EntityType = "part",
            EntityId = request.PartId,
            Quantity = request.IssueType == MaterialIssueType.Return ? request.Quantity : -request.Quantity,
            LotNumber = bin.LotNumber,
            FromLocationId = request.IssueType == MaterialIssueType.Return ? null : bin.LocationId,
            ToLocationId = request.IssueType == MaterialIssueType.Return ? bin.LocationId : null,
            Reason = BinMovementReason.Adjustment,
            MovedBy = request.IssuedById,
            MovedAt = now,
        });

        return [new StockDraw(bin.LotNumber ?? request.LotNumber, request.Quantity, bin.Id, null)];
    }

    private async Task<List<StockDraw>> DrawFromLocationAsync(
        CreateMaterialIssueCommand request, DateTimeOffset now, CancellationToken ct)
    {
        var lotNumber = string.IsNullOrWhiteSpace(request.LotNumber) ? null : request.LotNumber;
        if (request.IssueType == MaterialIssueType.Issue && lotNumber is not null)
        {
            var lotRows = db.BinContents.Where(bc => bc.EntityType == "part"
                && bc.EntityId == request.PartId
                && bc.LotNumber == lotNumber
                && bc.RemovedAt == null
                && bc.Quantity > 0);
            if (request.StorageLocationId is int lotLocationId)
                lotRows = lotRows.Where(bc => bc.LocationId == lotLocationId);

            if (await lotRows.AnyAsync(bc => bc.Status == BinContentStatus.QcHold, ct)
                && !await lotRows.AnyAsync(bc => bc.Status != BinContentStatus.QcHold, ct))
                throw await LotQualityHold.RefusalAsync(db, request.PartId, lotNumber, ct);
        }

        if (request.StorageLocationId is not int locationId)
            throw new InvalidOperationException("Choose the bin or location the material comes from.");

        if (request.IssueType == MaterialIssueType.Return)
            return [await RestoreAsync(request, locationId, lotNumber, now, ct)];

        var rows = (await ScanBinStock.ActiveRowsAsync(db, request.PartId, locationId, ct))
            .Where(bc => lotNumber is null || bc.LotNumber == lotNumber)
            .ToList();
        var expiredLots = request.IssueType == MaterialIssueType.Issue
            ? await LotExpiry.ExpiredLotsAsync(db, request.PartId, await LotExpiry.ShopTodayAsync(db, now, ct), ct)
            : [];
        if (request.IssueType == MaterialIssueType.Issue)
            rows = await IssuableAsync(request, rows, expiredLots, ct);

        var free = rows.Sum(bc => bc.Quantity - bc.ReservedQuantity);
        if (free < request.Quantity)
            throw new InvalidOperationException(
                $"Insufficient quantity at the location. Available: {free}, Requested: {request.Quantity}");

        var drawn = BinContentDrawDown.Take(rows, request.Quantity, request.IssuedById, now);
        foreach (var (row, taken) in drawn)
            AddMovement(request, row.LotNumber, -taken, locationId, now);
        foreach (var lot in drawn.Select(d => d.Row.LotNumber).OfType<string>().Distinct())
        {
            if (expiredLots.TryGetValue(lot, out var expiredOn))
                LogExpiredOverride(request, lot, expiredOn, now);
        }

        return drawn
            .GroupBy(d => d.Row.LotNumber)
            .Select(g => new StockDraw(g.Key, g.Sum(d => d.Taken), null, g.Count() == 1 ? g.First().Row : null))
            .ToList();
    }

    private async Task<StockDraw> RestoreAsync(
        CreateMaterialIssueCommand request, int locationId, string? lotNumber, DateTimeOffset now, CancellationToken ct)
    {
        var restored = await ScanBinStock.AddAsync(
            db, request.PartId, locationId, lotNumber, request.Quantity, request.IssuedById, now, ct);
        if (restored.Status == BinContentStatus.Stored
            && await LotQualityHold.IsHeldAsync(db, request.PartId, lotNumber, ct))
            restored.Status = BinContentStatus.QcHold;
        AddMovement(request, lotNumber, request.Quantity, locationId, now);
        return new StockDraw(lotNumber, request.Quantity, null, restored);
    }

    private async Task<List<BinContent>> IssuableAsync(
        CreateMaterialIssueCommand request, List<BinContent> rows, Dictionary<string, DateOnly> expiredLots,
        CancellationToken ct)
    {
        bool IsExpired(BinContent bc) => bc.LotNumber is not null && expiredLots.ContainsKey(bc.LotNumber);

        var issuable = rows
            .Where(bc => bc.Status != BinContentStatus.QcHold && (request.AllowExpiredLot || !IsExpired(bc)))
            .ToList();
        var free = issuable.Sum(bc => bc.Quantity - bc.ReservedQuantity);
        if (free >= request.Quantity)
            return issuable;

        var held = rows.Where(bc => bc.Status == BinContentStatus.QcHold && bc.Quantity > 0).ToList();
        if (held.Count > 0 && free + held.Sum(bc => bc.Quantity) >= request.Quantity)
            throw await LotQualityHold.RefusalAsync(db, request.PartId, held[0].LotNumber, ct);

        var expired = rows
            .Where(bc => bc.Status != BinContentStatus.QcHold && IsExpired(bc) && bc.Quantity - bc.ReservedQuantity > 0)
            .ToList();
        if (expired.Count > 0)
            throw LotExpiry.Refusal(expired[0].LotNumber!, expiredLots[expired[0].LotNumber!]);
        return issuable;
    }

    private async Task RefuseExpiredLotAsync(
        CreateMaterialIssueCommand request, string? lotNumber, DateTimeOffset now, CancellationToken ct)
    {
        if (await LotExpiry.ExpiredOnAsync(db, request.PartId, lotNumber, now, ct) is not DateOnly expiredOn)
            return;
        if (!request.AllowExpiredLot)
            throw LotExpiry.Refusal(lotNumber!, expiredOn);
        LogExpiredOverride(request, lotNumber!, expiredOn, now);
    }

    private void LogExpiredOverride(
        CreateMaterialIssueCommand request, string lotNumber, DateOnly expiredOn, DateTimeOffset now)
        => db.JobActivityLogs.Add(new JobActivityLog
        {
            JobId = request.JobId,
            UserId = request.IssuedById,
            Action = ActivityAction.FieldChanged,
            FieldName = "MaterialIssue",
            NewValue = lotNumber,
            OperationId = request.OperationId,
            CreatedAt = now,
            Description = $"Issued lot {lotNumber}, expired on {expiredOn:yyyy-MM-dd}, by override.",
        });

    private void AddMovement(
        CreateMaterialIssueCommand request, string? lotNumber, decimal quantity, int locationId, DateTimeOffset now)
        => db.BinMovements.Add(new BinMovement
        {
            EntityType = "part",
            EntityId = request.PartId,
            Quantity = quantity,
            LotNumber = lotNumber,
            FromLocationId = quantity < 0 ? locationId : null,
            ToLocationId = quantity > 0 ? locationId : null,
            Reason = BinMovementReason.Adjustment,
            MovedBy = request.IssuedById,
            MovedAt = now,
        });

    private sealed record StockDraw(string? LotNumber, decimal Quantity, int? BinContentId, BinContent? Row);
}
