using System.Globalization;

using FluentValidation;
using FluentValidation.Results;
using Microsoft.EntityFrameworkCore;

using Forge.Api.Capabilities;
using Forge.Core.Entities;
using Forge.Core.Entities.Accounting;
using Forge.Core.Enums;
using Forge.Core.Enums.Accounting;
using Forge.Core.Interfaces;
using Forge.Core.Models.Accounting;
using Forge.Data.Context;
using Forge.Data.Extensions;

namespace Forge.Api.Features.Accounting.StockLoads;

/// <inheritdoc />
public sealed class StockLoadService(
    AppDbContext db,
    IPostingEngine postingEngine,
    IInventoryValuationService valuation,
    IInventoryRepository inventory,
    ICapabilitySnapshotProvider capabilities,
    IClock clock) : IStockLoadService
{
    internal const string OpeningBalanceEquityKey = "OPENING_BALANCE_EQUITY";
    internal const string LotsCapability = "CAP-INV-LOTS";
    internal const string ConnectedCapability = "CAP-ACCT-EXTERNAL";
    private const decimal StandardCostTolerance = 0.005m;

    private static readonly string[] InventoryKeys = ["INVENTORY_RAW", "INVENTORY_SUBASSEMBLY", "INVENTORY_FG"];

    public async Task<List<InventoryStockLoadLine>> ResolveLinesAsync(
        IReadOnlyList<StockLoadLineInput> input, CancellationToken ct = default)
    {
        var failures = new List<ValidationFailure>();

        var numbers = input.Where(l => l.PartId is null && !string.IsNullOrWhiteSpace(l.PartNumber))
            .Select(l => l.PartNumber!.Trim()).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        var numbersUpper = numbers.Select(n => n.ToUpperInvariant()).ToList();
        var partsByNumber = (await db.Parts.AsNoTracking()
                .Where(p => numbersUpper.Contains(p.PartNumber.ToUpper()))
                .Select(p => new { p.Id, p.PartNumber })
                .ToListAsync(ct))
            .GroupBy(p => p.PartNumber, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.First().Id, StringComparer.OrdinalIgnoreCase);
        var requestedPartIds = input.Where(l => l.PartId is not null).Select(l => l.PartId!.Value).Distinct().ToList();
        var knownPartIds = (await db.Parts.AsNoTracking()
                .Where(p => requestedPartIds.Contains(p.Id)).Select(p => p.Id).ToListAsync(ct))
            .ToHashSet();

        var locations = await db.StorageLocations.AsNoTracking()
            .Where(l => l.IsActive)
            .Select(l => new { l.Id, l.Name, l.Barcode, l.IsDefault })
            .ToListAsync(ct);
        int? defaultLocationId = locations.FirstOrDefault(l => l.IsDefault)?.Id;

        var lines = new List<InventoryStockLoadLine>();
        for (var i = 0; i < input.Count; i++)
        {
            var row = input[i];
            var label = $"Row {i + 1}";

            int? partId = row.PartId is int id
                ? knownPartIds.Contains(id) ? id : null
                : !string.IsNullOrWhiteSpace(row.PartNumber) && partsByNumber.TryGetValue(row.PartNumber.Trim(), out var byNumber)
                    ? byNumber
                    : null;
            if (partId is null)
                failures.Add(Failure(i, "partNumber", "stock-load-unknown-part",
                    $"{label}: no part numbered '{row.PartNumber ?? row.PartId?.ToString(CultureInfo.InvariantCulture)}'."));

            int? locationId = null;
            if (row.StorageLocationId is int requestedLocation)
            {
                locationId = locations.Any(l => l.Id == requestedLocation) ? requestedLocation : null;
                if (locationId is null)
                    failures.Add(Failure(i, "location", "stock-load-unknown-location",
                        $"{label}: location {requestedLocation} does not exist or is inactive."));
            }
            else if (string.IsNullOrWhiteSpace(row.Location))
            {
                defaultLocationId ??= (await inventory.EnsureDefaultLocationAsync(ct)).Id;
                locationId = defaultLocationId;
            }
            else
            {
                var key = row.Location.Trim();
                var byBarcode = locations.Where(l => string.Equals(l.Barcode, key, StringComparison.OrdinalIgnoreCase)).ToList();
                var matches = byBarcode.Count > 0
                    ? byBarcode
                    : locations.Where(l => string.Equals(l.Name.Trim(), key, StringComparison.OrdinalIgnoreCase)).ToList();
                if (matches.Count == 1)
                    locationId = matches[0].Id;
                else
                    failures.Add(matches.Count == 0
                        ? Failure(i, "location", "stock-load-unknown-location", $"{label}: no active location '{key}'.")
                        : Failure(i, "location", "stock-load-ambiguous-location",
                            $"{label}: {matches.Count} locations are named '{key}'; use the location's barcode."));
            }

            if (row.Quantity <= 0m)
                failures.Add(Failure(i, "quantity", "stock-load-quantity-positive", $"{label}: the quantity must be greater than zero."));
            if (row.UnitCost < 0m)
                failures.Add(Failure(i, "unitCost", "stock-load-cost-negative", $"{label}: the unit cost cannot be negative."));
            var lot = string.IsNullOrWhiteSpace(row.LotNumber) ? null : row.LotNumber.Trim();
            if (lot is { Length: > 100 })
                failures.Add(Failure(i, "lot", "lot-number-too-long", $"{label}: the lot number is longer than 100 characters."));

            if (partId is null || locationId is null)
                continue;

            lines.Add(new InventoryStockLoadLine
            {
                LineNumber = i + 1,
                PartId = partId.Value,
                StorageLocationId = locationId.Value,
                LotNumber = lot,
                ExpirationDate = row.ExpirationDate,
                Quantity = row.Quantity,
                UnitCost = row.UnitCost,
                ExtendedValue = Math.Round(row.Quantity * row.UnitCost, 2, MidpointRounding.AwayFromZero),
            });
        }

        if (failures.Count > 0)
            throw new ValidationException(failures);
        return lines;
    }

    public async Task<StockLoadValidationResult> ValidateAsync(InventoryStockLoad load, CancellationToken ct = default)
    {
        var issues = new List<StockLoadIssue>();
        var lines = load.Lines.OrderBy(l => l.LineNumber).ToList();

        if (capabilities.IsEnabled(ConnectedCapability))
            issues.Add(Error(0, "stock-load-connected-mode",
                "Opening stock loads are not available while the books are kept in the connected accounting system; set the quantities with the on-hand override instead."));
        if (lines.Count == 0)
            issues.Add(Error(0, "stock-load-no-lines", "The stock load has no lines."));

        var partIds = lines.Select(l => l.PartId).Distinct().ToList();
        var parts = await db.Parts.AsNoTracking()
            .Include(p => p.CurrentCostCalculation)
            .Where(p => partIds.Contains(p.Id))
            .ToDictionaryAsync(p => p.Id, ct);
        var locationIds = lines.Select(l => l.StorageLocationId).Distinct().ToList();
        var activeLocations = (await db.StorageLocations.AsNoTracking()
                .Where(l => locationIds.Contains(l.Id) && l.IsActive).Select(l => l.Id).ToListAsync(ct))
            .ToHashSet();
        var occupied = (await db.BinContents.AsNoTracking()
                .Where(c => c.EntityType == "part" && partIds.Contains(c.EntityId) && c.RemovedAt == null && c.Quantity > 0m)
                .Select(c => new { c.EntityId, c.LocationId })
                .ToListAsync(ct))
            .Select(c => (c.EntityId, c.LocationId))
            .ToHashSet();

        var book = await db.Books.AsNoTracking()
            .Where(b => b.Id == load.BookId)
            .Select(b => new { b.Id, b.IsActive, b.DefaultCostingMethod })
            .FirstOrDefaultAsync(ct);
        if (book is not { IsActive: true })
            issues.Add(Error(0, "stock-load-book-inactive", $"Book {load.BookId} does not exist or is inactive."));

        var accounts = await InventoryAccountsAsync(load.BookId, ct);
        var lotsOn = capabilities.IsEnabled(LotsCapability);
        var seen = new HashSet<(int, int, string)>();
        var totals = new Dictionary<int, decimal>();
        var unmapped = new HashSet<string>(StringComparer.Ordinal);

        foreach (var line in lines)
        {
            if (!parts.TryGetValue(line.PartId, out var part))
            {
                issues.Add(Error(line.LineNumber, "stock-load-unknown-part", $"Line {line.LineNumber}: the part no longer exists."));
                continue;
            }
            var label = $"Line {line.LineNumber} ({part.PartNumber})";

            if (!activeLocations.Contains(line.StorageLocationId))
                issues.Add(Error(line.LineNumber, "stock-load-unknown-location", $"{label}: the location no longer exists or is inactive."));

            if (!IsStocked(part))
            {
                issues.Add(Error(line.LineNumber, "stock-load-part-not-stocked",
                    $"{label}: {part.InventoryClass} parts and phantoms are not carried in inventory."));
                continue;
            }

            if (part.TraceabilityType == TraceabilityType.Serial)
                issues.Add(Error(line.LineNumber, "stock-load-serial-unsupported",
                    $"{label}: serial-tracked parts cannot be loaded as opening stock yet; receive them with their serial numbers instead."));
            if (lotsOn && part.TraceabilityType == TraceabilityType.Lot && line.LotNumber is null)
                issues.Add(Error(line.LineNumber, "stock-load-lot-required", $"{label}: the part is lot-tracked, so the line needs a lot number."));

            if (!seen.Add((line.PartId, line.StorageLocationId, line.LotNumber?.ToUpperInvariant() ?? string.Empty)))
                issues.Add(Error(line.LineNumber, "stock-load-duplicate-line",
                    $"{label}: the same part, location and lot appear on more than one line."));
            if (occupied.Contains((line.PartId, line.StorageLocationId)))
                issues.Add(Error(line.LineNumber, "stock-load-location-not-empty",
                    $"{label}: the location already holds this part. Opening loads are for empty locations; adjust existing stock with a count."));

            if (line.UnitCost == 0m)
                issues.Add(Warning(line.LineNumber, "stock-load-zero-cost", $"{label}: the unit cost is zero, so the line adds quantity without value."));
            var standard = part.ManualCostOverride ?? part.CurrentCostCalculation?.ResultAmount ?? 0m;
            if (book?.DefaultCostingMethod == CostingMethod.Standard && standard > 0m
                && Math.Abs(line.UnitCost - standard) / standard > StandardCostTolerance)
                issues.Add(Warning(line.LineNumber, "stock-load-standard-cost-differs",
                    $"{label}: the unit cost {line.UnitCost:0.####} differs from the standard cost {standard:0.####} by more than 0.5%. The load values the stock at the cost given."));

            var key = InventoryValuationService.StoreKeyFor(part.InventoryClass);
            if (!accounts.Rules.TryGetValue(key, out var accountId))
            {
                unmapped.Add(key);
                continue;
            }
            totals[accountId] = totals.GetValueOrDefault(accountId) + line.ExtendedValue;
        }

        foreach (var key in unmapped.Order())
            issues.Add(Error(0, "stock-load-unmapped-account", $"The book has no account mapped to {key}; map it before loading stock."));
        if (load.GlMode == StockLoadGlMode.OffsetEquity && !accounts.Rules.ContainsKey(OpeningBalanceEquityKey))
            issues.Add(Error(0, "stock-load-unmapped-account",
                $"The book has no account mapped to {OpeningBalanceEquityKey}; map it before loading stock."));

        var opening = await OpeningInventoryAsync(load.BookId, ct);
        if (opening.Date is DateOnly openingDate && openingDate != load.AsOfDate)
            issues.Add(Warning(0, "stock-load-date-differs",
                $"The as-of date {load.AsOfDate:yyyy-MM-dd} differs from the opening journal's date {openingDate:yyyy-MM-dd}."));

        if (load.GlMode == StockLoadGlMode.OffsetEquity && opening.ByAccount.Count > 0)
        {
            var covered = await ValuationOnlyTotalsAsync(load.BookId, load.Id, ct);
            foreach (var accountId in totals.Keys.Order())
            {
                var uncovered = opening.ByAccount.GetValueOrDefault(accountId) - covered.GetValueOrDefault(accountId);
                if (uncovered <= 0m)
                    continue;
                var account = accounts.Names.GetValueOrDefault(accountId);
                issues.Add(Warning(0, "stock-load-opening-journal-has-inventory",
                    $"The posted opening journal already debited {uncovered:0.00} to {account.Number} {account.Name}. Posting this load against Opening Balance Equity adds its value a second time; choose valuation only to load the matching stock without a journal."));
            }
        }

        if (load.GlMode == StockLoadGlMode.ValuationOnly)
        {
            if (opening.ByAccount.Count == 0)
                issues.Add(Error(0, "stock-load-no-opening-inventory",
                    "Valuation only is for a book whose posted opening journal already debited inventory, and this book's has no inventory lines."));
            var earlier = await ValuationOnlyTotalsAsync(load.BookId, load.Id, ct);
            foreach (var (accountId, total) in totals)
            {
                var cap = opening.ByAccount.GetValueOrDefault(accountId);
                var used = earlier.GetValueOrDefault(accountId);
                if (opening.ByAccount.Count > 0 && used + total > cap)
                {
                    var account = accounts.Names.GetValueOrDefault(accountId);
                    issues.Add(Error(0, "stock-load-exceeds-opening-journal",
                        $"Valuation-only loads into {account.Number} {account.Name} would total {used + total:0.00}, more than the {cap:0.00} the opening journal debited to it."));
                }
            }
        }

        var accountTotals = totals
            .Select(t => accounts.Names.TryGetValue(t.Key, out var a)
                ? new StockLoadAccountTotal(t.Key, a.Number, a.Name, t.Value)
                : new StockLoadAccountTotal(t.Key, null, null, t.Value))
            .OrderBy(t => t.AccountNumber, StringComparer.Ordinal)
            .ToList();

        return new StockLoadValidationResult(
            issues.All(i => i.Severity != StockLoadIssueSeverity.Error),
            issues.OrderBy(i => i.LineNumber).ToList(),
            accountTotals,
            lines.Sum(l => l.ExtendedValue));
    }

    public async Task PostAsync(int stockLoadId, int userId, CancellationToken ct = default)
    {
        await using var tx = db.Database.CurrentTransaction is null ? await db.Database.BeginTransactionAsync(ct) : null;
        await db.AcquireAdvisoryLockAsync("inventory.stock-load", stockLoadId.ToString(CultureInfo.InvariantCulture), ct);

        var load = await db.InventoryStockLoads.Include(l => l.Lines)
            .FirstOrDefaultAsync(l => l.Id == stockLoadId, ct)
            ?? throw new KeyNotFoundException($"Stock load {stockLoadId} not found.");
        if (load.Status != StockLoadStatus.Draft)
            throw new InvalidOperationException($"Stock load {load.LoadNumber} is {load.Status}; only a draft can be posted.");

        var partIds = load.Lines.Select(l => l.PartId).Distinct().Order().ToList();
        foreach (var partId in partIds)
            await db.AcquireAdvisoryLockAsync("inventory.ship-relief", partId.ToString(CultureInfo.InvariantCulture), ct);
        if (load.GlMode == StockLoadGlMode.ValuationOnly)
            await db.AcquireAdvisoryLockAsync("inventory.stock-load.valuation-only", load.BookId.ToString(CultureInfo.InvariantCulture), ct);

        var result = await ValidateAsync(load, ct);
        if (!result.IsValid)
            throw ToValidationException(result);

        var parts = await db.Parts.AsNoTracking().Where(p => partIds.Contains(p.Id)).ToDictionaryAsync(p => p.Id, ct);
        var now = clock.UtcNow;
        var note = $"Opening stock load {load.LoadNumber}";
        var created = new List<(InventoryStockLoadLine Line, BinContent Content, BinMovement Movement)>();
        foreach (var line in load.Lines.OrderBy(l => l.LineNumber))
        {
            var content = new BinContent
            {
                EntityType = "part",
                EntityId = line.PartId,
                LocationId = line.StorageLocationId,
                Quantity = line.Quantity,
                LotNumber = line.LotNumber,
                Status = BinContentStatus.Stored,
                PlacedBy = userId,
                PlacedAt = now,
                Notes = note,
            };
            var movement = new BinMovement
            {
                EntityType = "part",
                EntityId = line.PartId,
                Quantity = line.Quantity,
                LotNumber = line.LotNumber,
                ToLocationId = line.StorageLocationId,
                MovedBy = userId,
                MovedAt = now,
                Reason = BinMovementReason.Adjustment,
                Notes = note,
            };
            db.BinContents.Add(content);
            db.BinMovements.Add(movement);
            created.Add((line, content, movement));

            if (line.LotNumber is not null && parts[line.PartId].TraceabilityType == TraceabilityType.Lot)
                await AddToLotAsync(parts[line.PartId], line, note, ct);
        }
        await db.SaveChangesAsync(ct);
        foreach (var (line, content, movement) in created)
        {
            line.BinContentId = content.Id;
            line.BinMovementId = movement.Id;
        }

        foreach (var group in load.Lines.GroupBy(l => l.PartId).OrderBy(g => g.Key))
            await valuation.ApplyReceiptAsync(load.BookId, group.Key, group.Sum(l => l.Quantity), group.Sum(l => l.ExtendedValue), ct);

        var total = load.Lines.Sum(l => l.ExtendedValue);
        if (load.GlMode == StockLoadGlMode.OffsetEquity && total > 0m)
        {
            var currencyId = await db.Books.AsNoTracking()
                .Where(b => b.Id == load.BookId).Select(b => b.FunctionalCurrencyId).FirstAsync(ct);
            var description = $"Opening stock {load.LoadNumber}";
            var debitLines = result.AccountTotals
                .Where(t => t.Total > 0m && t.GlAccountId is not null)
                .Select(t => new PostingLine { GlAccountId = t.GlAccountId, Debit = t.Total, Description = description });
            var entry = await postingEngine.PostAsync(new PostingRequest
            {
                BookId = load.BookId,
                EntryDate = load.AsOfDate,
                Source = JournalSource.Conversion,
                SourceType = InventoryValuationService.StockLoadSourceType,
                SourceId = load.Id,
                CurrencyId = currencyId,
                Memo = $"Opening stock load {load.LoadNumber} as of {load.AsOfDate:yyyy-MM-dd}",
                IdempotencyKey = $"{JournalSource.Conversion}:StockLoad:{load.Id}",
                AllowSoftClosedOverride = true,
                Lines =
                [
                    .. debitLines,
                    new PostingLine { AccountKey = OpeningBalanceEquityKey, Credit = total, Description = description },
                ],
            }, userId, ct);
            load.JournalEntryId = entry.Id;
        }

        load.Status = StockLoadStatus.Posted;
        load.TotalValue = total;
        load.PostedAt = now;
        load.PostedByUserId = userId;
        db.LogActivityAt("posted",
            $"Posted opening stock load {load.LoadNumber}: {load.Lines.Count} line(s), {total:0.00}",
            ("InventoryStockLoad", load.Id));
        await db.SaveChangesAsync(ct);

        if (tx is not null)
            await tx.CommitAsync(ct);
    }

    public async Task VoidAsync(int stockLoadId, int userId, CancellationToken ct = default)
    {
        await using var tx = db.Database.CurrentTransaction is null ? await db.Database.BeginTransactionAsync(ct) : null;
        await db.AcquireAdvisoryLockAsync("inventory.stock-load", stockLoadId.ToString(CultureInfo.InvariantCulture), ct);

        var load = await db.InventoryStockLoads.Include(l => l.Lines)
            .FirstOrDefaultAsync(l => l.Id == stockLoadId, ct)
            ?? throw new KeyNotFoundException($"Stock load {stockLoadId} not found.");
        if (load.Status != StockLoadStatus.Posted)
            throw new InvalidOperationException($"Stock load {load.LoadNumber} is {load.Status}; only a posted load can be voided.");

        var partIds = load.Lines.Select(l => l.PartId).Distinct().Order().ToList();
        foreach (var partId in partIds)
            await db.AcquireAdvisoryLockAsync("inventory.ship-relief", partId.ToString(CultureInfo.InvariantCulture), ct);

        var contentIds = load.Lines.Where(l => l.BinContentId is not null).Select(l => l.BinContentId!.Value).ToList();
        var contents = await db.BinContents.Where(c => contentIds.Contains(c.Id)).ToDictionaryAsync(c => c.Id, ct);
        var failures = new List<ValidationFailure>();
        foreach (var line in load.Lines.OrderBy(l => l.LineNumber))
        {
            var content = line.BinContentId is int id ? contents.GetValueOrDefault(id) : null;
            if (content is null || content.RemovedAt is not null || content.Quantity - content.ReservedQuantity < line.Quantity)
                failures.Add(new ValidationFailure($"lines[{line.LineNumber - 1}].quantity",
                    $"Line {line.LineNumber}: the loaded quantity is no longer all on hand and unreserved at its location, so the load cannot be voided.")
                    { ErrorCode = "stock-load-void-stock-moved" });
        }
        if (failures.Count > 0)
            throw new ValidationException(failures);

        var parts = await db.Parts.AsNoTracking().Where(p => partIds.Contains(p.Id)).ToDictionaryAsync(p => p.Id, ct);
        var now = clock.UtcNow;
        var note = $"Void of opening stock load {load.LoadNumber}";
        foreach (var line in load.Lines.OrderBy(l => l.LineNumber))
        {
            var content = contents[line.BinContentId!.Value];
            content.Quantity -= line.Quantity;
            if (content.Quantity == 0m)
            {
                content.RemovedAt = now;
                content.RemovedBy = userId;
            }
            db.BinMovements.Add(new BinMovement
            {
                EntityType = "part",
                EntityId = line.PartId,
                Quantity = line.Quantity,
                LotNumber = line.LotNumber,
                FromLocationId = line.StorageLocationId,
                MovedBy = userId,
                MovedAt = now,
                Reason = BinMovementReason.Reversal,
                ReversedMovementId = line.BinMovementId,
                Notes = note,
            });

            if (line.LotNumber is not null && parts[line.PartId].TraceabilityType == TraceabilityType.Lot)
            {
                var lot = await db.LotRecords
                    .FirstOrDefaultAsync(l => l.PartId == line.PartId && l.LotNumber == line.LotNumber, ct);
                if (lot is not null)
                    lot.Quantity = Math.Max(0m, lot.Quantity - line.Quantity);
            }
        }
        await db.SaveChangesAsync(ct);

        foreach (var group in load.Lines.GroupBy(l => l.PartId).OrderBy(g => g.Key))
            await valuation.ReverseReceiptAsync(load.BookId, group.Key, group.Sum(l => l.Quantity), group.Sum(l => l.ExtendedValue), ct);

        var original = load.JournalEntryId is long entryId
            ? await db.JournalEntries.AsNoTracking().Include(e => e.Lines).FirstAsync(e => e.Id == entryId, ct)
            : null;
        if (original is { Status: JournalEntryStatus.Posted, ReversedByEntryId: null })
        {
            var entryDate = await VoidEntryDateAsync(load.BookId, load.AsOfDate, DateOnly.FromDateTime(now.UtcDateTime), ct);
            await postingEngine.PostAsync(new PostingRequest
            {
                BookId = load.BookId,
                EntryDate = entryDate,
                Source = JournalSource.Conversion,
                SourceType = InventoryValuationService.StockLoadVoidSourceType,
                SourceId = load.Id,
                CurrencyId = original.CurrencyId,
                Memo = $"Void of opening stock load {load.LoadNumber}",
                IdempotencyKey = $"{JournalSource.Conversion}:StockLoad:{load.Id}:VOID",
                AllowSoftClosedOverride = true,
                Lines = original.Lines.OrderBy(l => l.LineNumber).Select(l => new PostingLine
                {
                    GlAccountId = l.GlAccountId,
                    Debit = l.Credit,
                    Credit = l.Debit,
                    Description = $"Void — {l.Description}",
                }).ToList(),
            }, userId, ct);
        }

        load.Status = StockLoadStatus.Voided;
        load.VoidedAt = now;
        db.LogActivityAt("voided", $"Voided opening stock load {load.LoadNumber}", ("InventoryStockLoad", load.Id));
        await db.SaveChangesAsync(ct);

        if (tx is not null)
            await tx.CommitAsync(ct);
    }

    public async Task<string> NextLoadNumberAsync(CancellationToken ct = default)
    {
        if (db.Database.CurrentTransaction is not null)
            await db.AcquireAdvisoryLockAsync("inventory-stock-loads.number", string.Empty, ct);

        var next = await db.InventoryStockLoads.IgnoreQueryFilters().CountAsync(ct) + 1;
        while (true)
        {
            var candidate = $"SL-{next:D6}";
            if (!await db.InventoryStockLoads.IgnoreQueryFilters().AnyAsync(l => l.LoadNumber == candidate, ct))
                return candidate;
            next++;
        }
    }

    public async Task<StockLoadContextModel> GetContextAsync(int? bookId, CancellationToken ct = default)
    {
        var books = db.Books.AsNoTracking().Where(b => b.IsActive);
        if (bookId is int requested)
            books = books.Where(b => b.Id == requested);
        var book = await books.OrderBy(b => b.Id).Select(b => new { b.Id, b.Name }).FirstOrDefaultAsync(ct)
            ?? throw new KeyNotFoundException(bookId is int id ? $"Book {id} not found or inactive." : "No active accounting book exists.");

        var opening = await OpeningInventoryAsync(book.Id, ct);
        var accounts = await InventoryAccountsAsync(book.Id, ct);
        var totals = opening.ByAccount
            .Select(kv => accounts.Names.TryGetValue(kv.Key, out var a)
                ? new StockLoadAccountTotal(kv.Key, a.Number, a.Name, kv.Value)
                : new StockLoadAccountTotal(kv.Key, null, null, kv.Value))
            .OrderBy(t => t.AccountNumber, StringComparer.Ordinal)
            .ToList();
        return new StockLoadContextModel(book.Id, book.Name, opening.Date, totals, capabilities.IsEnabled(ConnectedCapability));
    }

    public async Task<StockLoadModel> GetModelAsync(int stockLoadId, CancellationToken ct = default)
    {
        var load = await db.InventoryStockLoads.AsNoTracking()
            .FirstOrDefaultAsync(l => l.Id == stockLoadId, ct)
            ?? throw new KeyNotFoundException($"Stock load {stockLoadId} not found.");

        var lines = await db.InventoryStockLoadLines.AsNoTracking()
            .Where(l => l.StockLoadId == stockLoadId)
            .OrderBy(l => l.LineNumber)
            .Select(l => new StockLoadLineModel(
                l.Id, l.LineNumber, l.PartId, l.Part.PartNumber, l.Part.Name,
                l.StorageLocationId, l.StorageLocation.Name, l.LotNumber, l.ExpirationDate,
                l.Quantity, l.UnitCost, l.ExtendedValue))
            .ToListAsync(ct);

        long? entryNumber = load.JournalEntryId is long entryId
            ? await db.JournalEntries.AsNoTracking().IgnoreQueryFilters()
                .Where(e => e.Id == entryId).Select(e => (long?)e.EntryNumber).FirstOrDefaultAsync(ct)
            : null;

        return new StockLoadModel(
            load.Id, load.LoadNumber, load.BookId, load.AsOfDate, load.Status, load.GlMode,
            load.SourceFileName, load.Notes, load.Status == StockLoadStatus.Draft ? lines.Sum(l => l.ExtendedValue) : load.TotalValue,
            load.JournalEntryId, entryNumber, load.PostedAt, load.VoidedAt, load.CreatedAt, load.Version, lines);
    }

    internal static ValidationException ToValidationException(StockLoadValidationResult result)
        => new(result.Issues
            .Where(i => i.Severity == StockLoadIssueSeverity.Error)
            .Select(i => new ValidationFailure(
                i.LineNumber > 0 ? $"lines[{i.LineNumber - 1}]" : "stockLoad", i.Message) { ErrorCode = i.Code }));

    private async Task AddToLotAsync(Part part, InventoryStockLoadLine line, string note, CancellationToken ct)
    {
        var lot = await db.LotRecords
            .FirstOrDefaultAsync(l => l.PartId == line.PartId && l.LotNumber == line.LotNumber, ct);
        var expiration = line.ExpirationDate is DateOnly date
            ? new DateTimeOffset(date.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero)
            : (DateTimeOffset?)null;
        if (lot is not null)
        {
            lot.Quantity += line.Quantity;
            lot.ExpirationDate ??= expiration;
            return;
        }
        db.LotRecords.Add(new LotRecord
        {
            LotNumber = line.LotNumber!,
            PartId = line.PartId,
            PartRevision = part.Revision,
            Quantity = line.Quantity,
            ExpirationDate = expiration,
            Notes = note,
        });
    }

    private async Task<(Dictionary<string, int> Rules, Dictionary<int, (string Number, string Name)> Names)> InventoryAccountsAsync(
        int bookId, CancellationToken ct)
    {
        var keys = InventoryKeys.Append(OpeningBalanceEquityKey).ToList();
        var rules = await db.AccountDeterminationRules.AsNoTracking()
            .Where(r => r.BookId == bookId && keys.Contains(r.Key))
            .Select(r => new { r.Key, r.GlAccountId })
            .ToListAsync(ct);
        var byKey = rules.GroupBy(r => r.Key).ToDictionary(g => g.Key, g => g.First().GlAccountId, StringComparer.Ordinal);
        var names = await db.GlAccounts.AsNoTracking().IgnoreQueryFilters()
            .Where(a => a.BookId == bookId && a.ControlType == ControlAccountType.Inventory)
            .Select(a => new { a.Id, a.AccountNumber, a.Name })
            .ToDictionaryAsync(a => a.Id, a => (a.AccountNumber, a.Name), ct);
        return (byKey, names);
    }

    private async Task<(DateOnly? Date, Dictionary<int, decimal> ByAccount)> OpeningInventoryAsync(int bookId, CancellationToken ct)
    {
        var openingTypes = new[] { "OpeningJournal", "OpeningBalance" };
        var date = await db.JournalEntries.AsNoTracking().IgnoreQueryFilters()
            .Where(e => e.BookId == bookId && e.Source == JournalSource.Conversion
                && e.Status == JournalEntryStatus.Posted && openingTypes.Contains(e.SourceType!))
            .OrderBy(e => e.Id)
            .Select(e => (DateOnly?)e.EntryDate)
            .FirstOrDefaultAsync(ct);

        var rows = await
            (from line in db.JournalLines.IgnoreQueryFilters()
             join entry in db.JournalEntries.IgnoreQueryFilters() on line.JournalEntryId equals entry.Id
             join account in db.GlAccounts.IgnoreQueryFilters() on line.GlAccountId equals account.Id
             where entry.BookId == bookId
                 && entry.Source == JournalSource.Conversion
                 && entry.Status == JournalEntryStatus.Posted
                 && openingTypes.Contains(entry.SourceType!)
                 && account.ControlType == ControlAccountType.Inventory
             select new { line.GlAccountId, Amount = line.Debit > 0 ? line.FunctionalAmount : -line.FunctionalAmount })
            .ToListAsync(ct);

        var byAccount = rows.GroupBy(r => r.GlAccountId)
            .Select(g => (g.Key, Total: g.Sum(r => r.Amount)))
            .Where(t => t.Total > 0m)
            .ToDictionary(t => t.Key, t => t.Total);
        return (date, byAccount);
    }

    private async Task<Dictionary<int, decimal>> ValuationOnlyTotalsAsync(int bookId, int excludeLoadId, CancellationToken ct)
    {
        var rules = (await InventoryAccountsAsync(bookId, ct)).Rules;
        var rows = await db.InventoryStockLoadLines.AsNoTracking()
            .Where(l => l.StockLoad.BookId == bookId
                && l.StockLoadId != excludeLoadId
                && l.StockLoad.Status == StockLoadStatus.Posted
                && l.StockLoad.GlMode == StockLoadGlMode.ValuationOnly)
            .Select(l => new { l.Part.InventoryClass, l.ExtendedValue })
            .ToListAsync(ct);
        return rows
            .Where(r => rules.ContainsKey(InventoryValuationService.StoreKeyFor(r.InventoryClass)))
            .GroupBy(r => rules[InventoryValuationService.StoreKeyFor(r.InventoryClass)])
            .ToDictionary(g => g.Key, g => g.Sum(r => r.ExtendedValue));
    }

    private async Task<DateOnly> VoidEntryDateAsync(int bookId, DateOnly asOf, DateOnly today, CancellationToken ct)
    {
        var status = await
            (from period in db.FiscalPeriods
             join year in db.FiscalYears on period.FiscalYearId equals year.Id
             where year.BookId == bookId && period.StartDate <= asOf && period.EndDate >= asOf
             select (FiscalPeriodStatus?)period.Status)
            .FirstOrDefaultAsync(ct);
        return status is null or FiscalPeriodStatus.HardClosed ? today : asOf;
    }

    private static bool IsStocked(Part part)
        => part.ProcurementSource != ProcurementSource.Phantom
            && part.InventoryClass is InventoryClass.Raw or InventoryClass.Component
                or InventoryClass.Subassembly or InventoryClass.FinishedGood;

    private static StockLoadIssue Error(int line, string code, string message)
        => new(line, code, StockLoadIssueSeverity.Error, message);

    private static StockLoadIssue Warning(int line, string code, string message)
        => new(line, code, StockLoadIssueSeverity.Warning, message);

    private static ValidationFailure Failure(int index, string field, string code, string message)
        => new($"lines[{index}].{field}", message) { ErrorCode = code };
}
