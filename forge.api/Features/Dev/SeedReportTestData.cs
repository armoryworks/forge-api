using Microsoft.EntityFrameworkCore;
using Forge.Core.Entities;
using Forge.Core.Interfaces;
using Forge.Data.Context;
using Serilog;

namespace Forge.Api.Features.Dev;

/// <summary>
/// Phase 3 / WU-16 / H7 — dev-only seed extension that populates representative
/// source data for each major report category, so report cases (RPT-LOTTRACE-001/002,
/// RPT-VENDORPERF-001, RPT-CYCLECNT-001, RPT-MRPEX-001/002, RPT-OEE-001/003,
/// RPT-PMSUM-001, RPT-SCRAPRWK-001, RPT-SCHEDATT-001) can verdict against
/// measurable data instead of empty placeholders.
///
/// Idempotent. Sentinel-prefixed (RT-SEED-) for traceability and clean removal.
/// Best-effort per category — skips when prerequisites aren't met.
/// </summary>
/// <summary>Request body for POST /api/v1/dev/seed-report-test-data.</summary>
public record SeedReportTestDataRequest(string? Scope);

[Flags]
public enum SeedScope
{
    None             = 0,
    CompletedJobs    = 1 << 0,
    Ncrs             = 1 << 1,
    WorkCenters      = 1 << 2,
    PmSchedules      = 1 << 3,
    MrpExceptions    = 1 << 4,
    RealReceipts     = 1 << 5,
    LotConsumption   = 1 << 6,
    FinishedSerials  = 1 << 7,
    All              = CompletedJobs | Ncrs | WorkCenters | PmSchedules
                       | MrpExceptions | RealReceipts | LotConsumption | FinishedSerials,
}

public static class SeedScopeParser
{
    public static SeedScope Parse(string? token) => (token ?? "").Trim().ToLowerInvariant() switch
    {
        "all"               => SeedScope.All,
        "completed-jobs"    => SeedScope.CompletedJobs,
        "ncrs"              => SeedScope.Ncrs,
        "work-centers"      => SeedScope.WorkCenters,
        "pm-schedules"      => SeedScope.PmSchedules,
        "mrp-exceptions"    => SeedScope.MrpExceptions,
        "real-receipts"     => SeedScope.RealReceipts,
        "lot-consumption"   => SeedScope.LotConsumption,
        "finished-serials"  => SeedScope.FinishedSerials,
        _ => SeedScope.None,
    };
}

public class SeedReportTestData
{
    public const string SentinelPrefix = "RT-SEED-";
    private readonly AppDbContext _db;
    private readonly IClock _clock;

    public SeedReportTestData(AppDbContext db, IClock clock)
    {
        _db = db;
        _clock = clock;
    }

    /// <summary>
    /// Seed report-test source data. Returns a per-category summary of records created.
    /// Idempotent: re-running with the same scope is a no-op (sentinel-prefixed lookups
    /// short-circuit if already present).
    /// </summary>
    public async Task<Dictionary<string, int>> SeedAsync(SeedScope scope, CancellationToken ct)
    {
        var summary = new Dictionary<string, int>();
        // Suppress audit chatter while bulk-inserting synthetic test data.
        var savedSuppress = _db.SuppressAudit;
        _db.SuppressAudit = true;
        try
        {
            // Order matters — work centers and PM schedules underpin completed-jobs / OEE / maintenance.
            if (scope.HasFlag(SeedScope.WorkCenters))
                summary["work-centers"] = await SeedWorkCentersAsync(ct);

            if (scope.HasFlag(SeedScope.CompletedJobs))
                summary["completed-jobs"] = await SeedCompletedJobsAsync(ct);

            if (scope.HasFlag(SeedScope.Ncrs))
                summary["ncrs"] = await SeedNcrsAsync(ct);

            if (scope.HasFlag(SeedScope.PmSchedules))
                summary["pm-schedules"] = await SeedPmSchedulesAsync(ct);

            if (scope.HasFlag(SeedScope.MrpExceptions))
                summary["mrp-exceptions"] = await SeedMrpExceptionsAsync(ct);

            if (scope.HasFlag(SeedScope.RealReceipts))
                summary["real-receipts"] = await SeedRealReceiptsAsync(ct);

            if (scope.HasFlag(SeedScope.LotConsumption))
                summary["lot-consumption"] = await SeedLotConsumptionAsync(ct);

            if (scope.HasFlag(SeedScope.FinishedSerials))
                summary["finished-serials"] = await SeedFinishedSerialsAsync(ct);
        }
        finally
        {
            _db.SuppressAudit = savedSuppress;
        }
        return summary;
    }

    /// <summary>
    /// Best-effort cleanup of every sentinel-prefixed record this seed creates.
    /// Logs (does not throw) when FK constraints prevent removal of a category.
    /// Returns per-category removed counts.
    /// </summary>
    public async Task<Dictionary<string, int>> CleanupAsync(CancellationToken ct)
    {
        var removed = new Dictionary<string, int>();
        var savedSuppress = _db.SuppressAudit;
        _db.SuppressAudit = true;

        // Order: most-dependent first so FK constraints don't block parents.
        await TryRemoveAsync("finished-serials", removed, async () =>
        {
            // Serials whose Notes start with sentinel; also their SerialHistory rows.
            var serials = await _db.SerialNumbers
                .Where(s => s.Notes != null && s.Notes.StartsWith(SentinelPrefix))
                .ToListAsync(ct);
            var serialIds = serials.Select(s => s.Id).ToList();
            var hist = await _db.SerialHistories.Where(h => serialIds.Contains(h.SerialNumberId)).ToListAsync(ct);
            _db.SerialHistories.RemoveRange(hist);
            _db.SerialNumbers.RemoveRange(serials);
            await _db.SaveChangesAsync(ct);
            return serials.Count;
        });

        await TryRemoveAsync("lot-consumption", removed, async () =>
        {
            var issues = await _db.MaterialIssues
                .Where(m => m.LotNumber != null && m.LotNumber.StartsWith(SentinelPrefix))
                .ToListAsync(ct);
            _db.MaterialIssues.RemoveRange(issues);
            var lots = await _db.LotRecords
                .Where(l => l.LotNumber.StartsWith(SentinelPrefix))
                .ToListAsync(ct);
            _db.LotRecords.RemoveRange(lots);
            await _db.SaveChangesAsync(ct);
            return issues.Count + lots.Count;
        });

        await TryRemoveAsync("real-receipts", removed, async () =>
        {
            // Receipts whose PO line belongs to a sentinel PO.
            var receipts = await _db.ReceivingRecords
                .Where(r => r.Notes != null && r.Notes.StartsWith(SentinelPrefix))
                .ToListAsync(ct);
            _db.ReceivingRecords.RemoveRange(receipts);
            await _db.SaveChangesAsync(ct);
            return receipts.Count;
        });

        await TryRemoveAsync("mrp-exceptions", removed, async () =>
        {
            var runs = await _db.MrpRuns.Where(r => r.RunNumber.StartsWith(SentinelPrefix)).ToListAsync(ct);
            var runIds = runs.Select(r => r.Id).ToList();
            var excs = await _db.MrpExceptions.Where(e => runIds.Contains(e.MrpRunId)).ToListAsync(ct);
            _db.MrpExceptions.RemoveRange(excs);
            _db.MrpRuns.RemoveRange(runs);
            await _db.SaveChangesAsync(ct);
            return excs.Count + runs.Count;
        });

        await TryRemoveAsync("pm-schedules", removed, async () =>
        {
            var schedules = await _db.MaintenanceSchedules
                .Where(s => s.Title.StartsWith(SentinelPrefix))
                .ToListAsync(ct);
            var schedIds = schedules.Select(s => s.Id).ToList();
            var logs = await _db.MaintenanceLogs.Where(l => schedIds.Contains(l.MaintenanceScheduleId)).ToListAsync(ct);
            _db.MaintenanceLogs.RemoveRange(logs);
            _db.MaintenanceSchedules.RemoveRange(schedules);
            await _db.SaveChangesAsync(ct);
            return schedules.Count + logs.Count;
        });

        await TryRemoveAsync("ncrs", removed, async () =>
        {
            var ncrs = await _db.NonConformances
                .Where(n => n.NcrNumber.StartsWith(SentinelPrefix))
                .ToListAsync(ct);
            _db.NonConformances.RemoveRange(ncrs);
            await _db.SaveChangesAsync(ct);
            return ncrs.Count;
        });

        await TryRemoveAsync("completed-jobs", removed, async () =>
        {
            var jobs = await _db.Jobs
                .Where(j => j.JobNumber.StartsWith(SentinelPrefix))
                .ToListAsync(ct);
            var jobIds = jobs.Select(j => j.Id).ToList();
            var runs = await _db.ProductionRuns.Where(r => jobIds.Contains(r.JobId)).ToListAsync(ct);
            _db.ProductionRuns.RemoveRange(runs);
            _db.Jobs.RemoveRange(jobs);
            await _db.SaveChangesAsync(ct);
            return jobs.Count + runs.Count;
        });

        await TryRemoveAsync("work-centers", removed, async () =>
        {
            var wcs = await _db.WorkCenters
                .Where(w => w.Code.StartsWith(SentinelPrefix))
                .ToListAsync(ct);
            _db.WorkCenters.RemoveRange(wcs);
            // Assets created by this seed (Asset.Name starts with sentinel) — only remove
            // those with no remaining references. Best-effort.
            var assets = await _db.Assets
                .Where(a => a.Name.StartsWith(SentinelPrefix))
                .ToListAsync(ct);
            _db.Assets.RemoveRange(assets);
            await _db.SaveChangesAsync(ct);
            return wcs.Count + assets.Count;
        });

        _db.SuppressAudit = savedSuppress;
        return removed;
    }

    private async Task TryRemoveAsync(string label, Dictionary<string, int> removed, Func<Task<int>> op)
    {
        try { removed[label] = await op(); }
        catch (Exception ex)
        {
            Log.Warning(ex, "Seed cleanup: failed to remove '{Category}' (best-effort)", label);
            removed[label] = -1;
        }
    }

    // ── work-centers ────────────────────────────────────────────────────────
    private async Task<int> SeedWorkCentersAsync(CancellationToken ct)
    {
        var existing = await _db.WorkCenters.CountAsync(w => w.Code.StartsWith(SentinelPrefix), ct);
        if (existing > 0) return 0;

        var locId = await _db.CompanyLocations.Select(l => (int?)l.Id).FirstOrDefaultAsync(ct);
        var now = _clock.UtcNow;
        var wcs = new List<WorkCenter>
        {
            new() { Name = SentinelPrefix + "Mill-Bay-1", Code = SentinelPrefix + "MILL1",  DailyCapacityHours = 8m,  EfficiencyPercent = 92m, NumberOfMachines = 1, LaborCostPerHour = 35m, BurdenRatePerHour = 25m, IdealCycleTimeSeconds = 60m, IsActive = true,  CompanyLocationId = locId, SortOrder = 1, CreatedAt = now, UpdatedAt = now },
            new() { Name = SentinelPrefix + "Mill-Bay-2", Code = SentinelPrefix + "MILL2",  DailyCapacityHours = 8m,  EfficiencyPercent = 85m, NumberOfMachines = 1, LaborCostPerHour = 35m, BurdenRatePerHour = 25m, IdealCycleTimeSeconds = 60m, IsActive = true,  CompanyLocationId = locId, SortOrder = 2, CreatedAt = now, UpdatedAt = now },
            new() { Name = SentinelPrefix + "Lathe-1",    Code = SentinelPrefix + "LATHE1", DailyCapacityHours = 16m, EfficiencyPercent = 88m, NumberOfMachines = 2, LaborCostPerHour = 32m, BurdenRatePerHour = 22m, IdealCycleTimeSeconds = 90m, IsActive = true,  CompanyLocationId = locId, SortOrder = 3, CreatedAt = now, UpdatedAt = now },
            new() { Name = SentinelPrefix + "Press-1",    Code = SentinelPrefix + "PRESS1", DailyCapacityHours = 8m,  EfficiencyPercent = 95m, NumberOfMachines = 1, LaborCostPerHour = 40m, BurdenRatePerHour = 30m, IdealCycleTimeSeconds = 30m, IsActive = true,  CompanyLocationId = locId, SortOrder = 4, CreatedAt = now, UpdatedAt = now },
        };
        _db.WorkCenters.AddRange(wcs);
        await _db.SaveChangesAsync(ct);
        return wcs.Count;
    }

    // ── completed-jobs ──────────────────────────────────────────────────────
    private async Task<int> SeedCompletedJobsAsync(CancellationToken ct)
    {
        var existing = await _db.Jobs.CountAsync(j => j.JobNumber.StartsWith(SentinelPrefix), ct);
        if (existing > 0) return 0;

        var trackType = await _db.TrackTypes.FirstOrDefaultAsync(t => t.IsDefault, ct)
                        ?? await _db.TrackTypes.FirstOrDefaultAsync(ct);
        if (trackType == null) { Log.Information("Seed: no TrackTypes — skipping completed-jobs"); return 0; }

        // Pick the terminal stage for that track type (or any).
        var terminalStage = await _db.JobStages
            .Where(s => s.TrackTypeId == trackType.Id)
            .OrderByDescending(s => s.SortOrder)
            .FirstOrDefaultAsync(ct);
        if (terminalStage == null) { Log.Information("Seed: no JobStages — skipping completed-jobs"); return 0; }

        var workCenters = await _db.WorkCenters
            .Where(w => w.IsActive)
            .OrderBy(w => w.Id)
            .Take(6)
            .ToListAsync(ct);
        var part = await _db.Parts.OrderBy(p => p.Id).FirstOrDefaultAsync(ct);

        var now = _clock.UtcNow;
        var jobs = new List<Job>();
        var runs = new List<ProductionRun>();
        // Mix of on-time, late, scrap-laden, multi-WC.
        for (int i = 0; i < 10; i++)
        {
            var dueDate    = now.AddDays(-30 + i);
            var startDate  = dueDate.AddDays(-7);
            // Half on-time, half late by 1-3 days.
            var completedDate = (i % 2 == 0) ? dueDate.AddDays(-1) : dueDate.AddDays(1 + (i % 3));
            // Two of the ten get a Scrap disposition.
            JobDisposition? disp = (i == 3 || i == 7) ? JobDisposition.Scrap
                                  : (i % 2 == 0 ? JobDisposition.ShipToCustomer : JobDisposition.AddToInventory);
            var job = new Job
            {
                JobNumber       = $"{SentinelPrefix}WO-{i + 1:000}",
                Title           = $"{SentinelPrefix}Test job {i + 1}",
                Description     = "RT-SEED test job for OEE / scrap / on-time-delivery / lead-time reports.",
                TrackTypeId     = trackType.Id,
                CurrentStageId  = terminalStage.Id,
                Priority        = JobPriority.Normal,
                StartDate       = startDate,
                DueDate         = dueDate,
                CompletedDate   = completedDate,
                IsArchived      = false,
                PartId          = part?.Id,
                Disposition     = disp,
                DispositionAt   = completedDate,
                EstimatedMaterialCost = 100m + i * 25m,
                EstimatedLaborCost    = 200m + i * 15m,
                EstimatedBurdenCost   = 50m,
                QuotedPrice           = 600m + i * 50m,
                CreatedAt       = now.AddDays(-30 + i),
                UpdatedAt       = completedDate,
            };
            jobs.Add(job);
        }
        _db.Jobs.AddRange(jobs);
        await _db.SaveChangesAsync(ct);

        // Production runs distributed across work centers (drives OEE / scrap).
        if (workCenters.Count > 0 && part != null)
        {
            for (int i = 0; i < jobs.Count; i++)
            {
                var wc = workCenters[i % workCenters.Count];
                var target = 100;
                var scrap  = (i == 3 || i == 7) ? 12 : (i % 3);
                var run = new ProductionRun
                {
                    JobId               = jobs[i].Id,
                    PartId              = part.Id,
                    WorkCenterId        = wc.Id,
                    RunNumber           = $"{SentinelPrefix}PR-{i + 1:000}",
                    TargetQuantity      = target,
                    CompletedQuantity   = target - scrap,
                    ScrapQuantity       = scrap,
                    ReworkQuantity      = (i % 4 == 0) ? 3 : 0,
                    Status              = ProductionRunStatus.Completed,
                    StartedAt           = jobs[i].StartDate,
                    CompletedAt         = jobs[i].CompletedDate,
                    SetupTimeMinutes    = 30m,
                    RunTimeMinutes      = 240m + (i * 5m),
                    IdealCycleTimeSeconds  = wc.IdealCycleTimeSeconds ?? 60m,
                    ActualCycleTimeSeconds = (wc.IdealCycleTimeSeconds ?? 60m) * (1m + (i % 5) * 0.05m),
                    CreatedAt           = jobs[i].StartDate ?? _clock.UtcNow,
                    UpdatedAt           = jobs[i].CompletedDate ?? _clock.UtcNow,
                };
                runs.Add(run);
            }
            _db.ProductionRuns.AddRange(runs);
            await _db.SaveChangesAsync(ct);
        }

        return jobs.Count;
    }

    // ── ncrs ────────────────────────────────────────────────────────────────
    private async Task<int> SeedNcrsAsync(CancellationToken ct)
    {
        var existing = await _db.NonConformances.CountAsync(n => n.NcrNumber.StartsWith(SentinelPrefix), ct);
        if (existing > 0) return 0;

        var part   = await _db.Parts.OrderBy(p => p.Id).FirstOrDefaultAsync(ct);
        if (part == null) { Log.Information("Seed: no Parts — skipping ncrs"); return 0; }

        var vendor   = await _db.Vendors.OrderBy(v => v.Id).FirstOrDefaultAsync(ct);
        var customer = await _db.Customers.OrderBy(c => c.Id).FirstOrDefaultAsync(ct);
        // FK target — any user. Without it the NCR record cannot reference a detector.
        var detector = await _db.Users.OrderBy(u => u.Id).Select(u => (int?)u.Id).FirstOrDefaultAsync(ct);
        if (detector == null) { Log.Information("Seed: no Users — skipping ncrs"); return 0; }

        var now = _clock.UtcNow;
        var ncrs = new List<NonConformance>();
        // Mix: severities by disposition code, types by Internal/Supplier/Customer, with/without CAPA.
        var combos = new (NcrType type, NcrDispositionCode disp, NcrDetectionStage stage)[]
        {
            (NcrType.Internal, NcrDispositionCode.Rework,         NcrDetectionStage.InProcess),
            (NcrType.Internal, NcrDispositionCode.Scrap,          NcrDetectionStage.FinalInspection),
            (NcrType.Supplier, NcrDispositionCode.ReturnToVendor, NcrDetectionStage.Receiving),
            (NcrType.Supplier, NcrDispositionCode.Reject,         NcrDetectionStage.Receiving),
            (NcrType.Internal, NcrDispositionCode.UseAsIs,        NcrDetectionStage.Audit),
            (NcrType.Customer, NcrDispositionCode.Rework,         NcrDetectionStage.Customer),
            (NcrType.Customer, NcrDispositionCode.Scrap,          NcrDetectionStage.Customer),
        };
        for (int i = 0; i < combos.Length; i++)
        {
            var c = combos[i];
            var detectedAt = now.AddDays(-21 + i * 2);
            var ncr = new NonConformance
            {
                NcrNumber           = $"{SentinelPrefix}NCR-{i + 1:000}",
                Type                = c.type,
                PartId              = part.Id,
                DetectedById        = detector.Value,
                DetectedAt          = detectedAt,
                DetectedAtStage     = c.stage,
                Description         = $"RT-SEED quality issue {i + 1}",
                AffectedQuantity    = 5m + i,
                DefectiveQuantity   = 1m + (i % 3),
                DispositionCode     = c.disp,
                DispositionAt       = detectedAt.AddDays(2),
                DispositionNotes    = $"RT-SEED disposition {c.disp}",
                Status              = (i % 2 == 0) ? NcrStatus.Closed : NcrStatus.Dispositioned,
                MaterialCost        = 25m * (i + 1),
                LaborCost           = 40m * (i + 1),
                TotalCostImpact     = 65m * (i + 1),
                CustomerId          = (c.type == NcrType.Customer) ? customer?.Id : null,
                VendorId            = (c.type == NcrType.Supplier) ? vendor?.Id : null,
                CreatedAt           = detectedAt,
                UpdatedAt           = detectedAt.AddDays(2),
            };
            ncrs.Add(ncr);
        }
        _db.NonConformances.AddRange(ncrs);
        await _db.SaveChangesAsync(ct);
        return ncrs.Count;
    }

    // ── pm-schedules ────────────────────────────────────────────────────────
    private async Task<int> SeedPmSchedulesAsync(CancellationToken ct)
    {
        var existing = await _db.MaintenanceSchedules.CountAsync(s => s.Title.StartsWith(SentinelPrefix), ct);
        if (existing > 0) return 0;

        var assets = await _db.Assets.OrderBy(a => a.Id).Take(6).ToListAsync(ct);
        if (assets.Count == 0)
        {
            // Seed a couple of assets ourselves so PM schedules can attach to something.
            var now0 = _clock.UtcNow;
            var seedAssets = new List<Asset>
            {
                new() { Name = SentinelPrefix + "Asset-Mill-1",  AssetType = AssetType.Machine, Status = AssetStatus.Active, CurrentHours = 1500m, CreatedAt = now0, UpdatedAt = now0 },
                new() { Name = SentinelPrefix + "Asset-Lathe-1", AssetType = AssetType.Machine, Status = AssetStatus.Active, CurrentHours = 980m,  CreatedAt = now0, UpdatedAt = now0 },
                new() { Name = SentinelPrefix + "Asset-Press-1", AssetType = AssetType.Machine, Status = AssetStatus.Active, CurrentHours = 2300m, CreatedAt = now0, UpdatedAt = now0 },
            };
            _db.Assets.AddRange(seedAssets);
            await _db.SaveChangesAsync(ct);
            assets = seedAssets;
        }

        var now = _clock.UtcNow;
        var schedules = new List<MaintenanceSchedule>();
        // Variety: completed (LastPerformedAt set & NextDueAt future), upcoming (NextDueAt soon),
        // overdue (NextDueAt in past, no recent log).
        for (int i = 0; i < Math.Min(6, assets.Count * 2); i++)
        {
            var asset = assets[i % assets.Count];
            DateTimeOffset next = (i % 3) switch
            {
                0 => now.AddDays(15),     // upcoming
                1 => now.AddDays(-7),     // overdue
                _ => now.AddDays(45),     // future / completed
            };
            DateTimeOffset? last = (i % 3 == 2) ? now.AddDays(-30) : (i % 3 == 0 ? now.AddDays(-60) : (DateTimeOffset?)null);
            var sched = new MaintenanceSchedule
            {
                AssetId          = asset.Id,
                Title            = $"{SentinelPrefix}PM-{i + 1:000}",
                Description      = "RT-SEED preventive maintenance schedule",
                IntervalDays     = 30 + (i * 7),
                LastPerformedAt  = last,
                NextDueAt        = next,
                IsActive         = true,
                CreatedAt        = now.AddDays(-90),
                UpdatedAt        = now,
            };
            schedules.Add(sched);
        }
        _db.MaintenanceSchedules.AddRange(schedules);
        await _db.SaveChangesAsync(ct);

        // Add a maintenance-log per completed schedule so the maintenance summary report has rows.
        var logs = new List<MaintenanceLog>();
        var performer = await _db.Users.OrderBy(u => u.Id).Select(u => (int?)u.Id).FirstOrDefaultAsync(ct);
        if (performer != null)
        {
            foreach (var s in schedules.Where(s => s.LastPerformedAt.HasValue))
            {
                logs.Add(new MaintenanceLog
                {
                    MaintenanceScheduleId = s.Id,
                    PerformedById         = performer.Value,
                    PerformedAt           = s.LastPerformedAt!.Value,
                    HoursAtService        = 100m,
                    Notes                 = "RT-SEED routine PM",
                    Cost                  = 250m,
                });
            }
            _db.MaintenanceLogs.AddRange(logs);
            await _db.SaveChangesAsync(ct);
        }

        return schedules.Count;
    }

    // ── mrp-exceptions ──────────────────────────────────────────────────────
    private async Task<int> SeedMrpExceptionsAsync(CancellationToken ct)
    {
        var existing = await _db.MrpExceptions
            .Where(e => e.MrpRun.RunNumber.StartsWith(SentinelPrefix))
            .CountAsync(ct);
        if (existing > 0) return 0;

        var parts = await _db.Parts.OrderBy(p => p.Id).Take(8).ToListAsync(ct);
        if (parts.Count == 0) { Log.Information("Seed: no Parts — skipping mrp-exceptions"); return 0; }

        var now = _clock.UtcNow;
        var run = new MrpRun
        {
            RunNumber           = $"{SentinelPrefix}MRP-001",
            RunType             = MrpRunType.Full,
            Status              = MrpRunStatus.Completed,
            IsSimulation        = false,
            StartedAt           = now.AddHours(-2),
            CompletedAt         = now.AddHours(-1),
            PlanningHorizonDays = 90,
            CreatedAt           = now.AddHours(-2),
            UpdatedAt           = now.AddHours(-1),
        };
        _db.MrpRuns.Add(run);
        await _db.SaveChangesAsync(ct);

        var types = new[]
        {
            MrpExceptionType.Expedite, MrpExceptionType.Defer,    MrpExceptionType.Cancel,
            MrpExceptionType.PastDue,  MrpExceptionType.ShortSupply, MrpExceptionType.OverSupply,
            MrpExceptionType.LeadTimeViolation, MrpExceptionType.ShortSupply,
        };
        var excs = new List<MrpException>();
        for (int i = 0; i < types.Length; i++)
        {
            var part = parts[i % parts.Count];
            excs.Add(new MrpException
            {
                MrpRunId        = run.Id,
                PartId          = part.Id,
                ExceptionType   = types[i],
                Message         = $"RT-SEED {types[i]} on part {part.PartNumber}",
                SuggestedAction = "RT-SEED suggested action",
                IsResolved      = false,
            });
        }
        run.ExceptionCount = excs.Count;
        _db.MrpExceptions.AddRange(excs);
        await _db.SaveChangesAsync(ct);
        return excs.Count;
    }

    // ── real-receipts ───────────────────────────────────────────────────────
    private async Task<int> SeedRealReceiptsAsync(CancellationToken ct)
    {
        // Find existing PO lines that have not yet been fully received and append
        // sentinel-marked receipts so vendor performance has data to aggregate.
        var lines = await _db.PurchaseOrderLines
            .Include(l => l.PurchaseOrder)
            .Where(l => l.ReceivedQuantity < l.OrderedQuantity
                        && l.PurchaseOrder.Status != PurchaseOrderStatus.Cancelled
                        && l.PurchaseOrder.Status != PurchaseOrderStatus.Draft
                        && l.PurchaseOrder.DeletedAt == null)
            .OrderBy(l => l.Id)
            .Take(10)
            .ToListAsync(ct);

        if (lines.Count == 0)
        {
            Log.Information("Seed: no eligible PO lines — skipping real-receipts");
            return 0;
        }

        // Idempotency: skip if any sentinel receipts already exist on these lines.
        var lineIds = lines.Select(l => l.Id).ToList();
        var alreadySeeded = await _db.ReceivingRecords
            .AnyAsync(r => lineIds.Contains(r.PurchaseOrderLineId)
                       && r.Notes != null && r.Notes.StartsWith(SentinelPrefix), ct);
        if (alreadySeeded) return 0;

        var now = _clock.UtcNow;
        var receipts = new List<ReceivingRecord>();
        for (int i = 0; i < lines.Count; i++)
        {
            var line = lines[i];
            var qty = (int)Math.Max(1, line.OrderedQuantity - line.ReceivedQuantity);
            // Vary on-time vs late vs full vs partial. Inspection: most pass, some fail.
            var inspect = (i % 5 == 0)
                ? ReceivingInspectionStatus.Failed
                : (i % 3 == 0 ? ReceivingInspectionStatus.PartialAccept : ReceivingInspectionStatus.Passed);
            var rejected = inspect == ReceivingInspectionStatus.Failed ? qty
                : inspect == ReceivingInspectionStatus.PartialAccept ? Math.Max(1, qty / 5)
                : 0;
            var accepted = qty - rejected;
            receipts.Add(new ReceivingRecord
            {
                PurchaseOrderLineId          = line.Id,
                QuantityReceived             = qty,
                Notes                        = $"{SentinelPrefix}receipt for vendor-performance test",
                InspectionStatus             = inspect,
                InspectedAt                  = now.AddDays(-i),
                InspectedQuantityAccepted    = accepted,
                InspectedQuantityRejected    = rejected,
                CreatedAt                    = now.AddDays(-i),
                UpdatedAt                    = now.AddDays(-i),
            });
            // Reflect in the line so the report sees consistent totals.
            line.ReceivedQuantity += qty;
        }
        _db.ReceivingRecords.AddRange(receipts);
        await _db.SaveChangesAsync(ct);
        return receipts.Count;
    }

    // ── lot-consumption ─────────────────────────────────────────────────────
    private async Task<int> SeedLotConsumptionAsync(CancellationToken ct)
    {
        var existing = await _db.LotRecords.CountAsync(l => l.LotNumber.StartsWith(SentinelPrefix), ct);
        if (existing > 0) return 0;

        var part = await _db.Parts.OrderBy(p => p.Id).FirstOrDefaultAsync(ct);
        if (part == null) { Log.Information("Seed: no Parts — skipping lot-consumption"); return 0; }

        // Prefer sentinel jobs (so lot-trace reports have a round-trip path with
        // controllable input). Fall back to any job if none are seeded yet.
        var jobs = await _db.Jobs
            .Where(j => j.JobNumber.StartsWith(SentinelPrefix))
            .OrderBy(j => j.Id)
            .Take(3)
            .ToListAsync(ct);
        if (jobs.Count == 0)
            jobs = await _db.Jobs.OrderBy(j => j.Id).Take(3).ToListAsync(ct);
        if (jobs.Count == 0) { Log.Information("Seed: no Jobs — skipping lot-consumption"); return 0; }

        var issuer = await _db.Users.OrderBy(u => u.Id).Select(u => (int?)u.Id).FirstOrDefaultAsync(ct);
        if (issuer == null) { Log.Information("Seed: no Users — skipping lot-consumption"); return 0; }

        var now = _clock.UtcNow;
        var lots = new List<LotRecord>
        {
            new() { LotNumber = $"{SentinelPrefix}LOT-A001", PartId = part.Id, Quantity = 100, ExpirationDate = now.AddYears(1),  CreatedAt = now.AddDays(-30), UpdatedAt = now.AddDays(-30) },
            new() { LotNumber = $"{SentinelPrefix}LOT-A002", PartId = part.Id, Quantity = 200, ExpirationDate = now.AddYears(1),  CreatedAt = now.AddDays(-20), UpdatedAt = now.AddDays(-20) },
            new() { LotNumber = $"{SentinelPrefix}LOT-B001", PartId = part.Id, Quantity = 150, ExpirationDate = now.AddYears(2),  CreatedAt = now.AddDays(-10), UpdatedAt = now.AddDays(-10) },
        };
        _db.LotRecords.AddRange(lots);
        await _db.SaveChangesAsync(ct);

        // Issue lots to jobs (consumption events) so backward / forward trace works.
        var issues = new List<MaterialIssue>();
        for (int i = 0; i < jobs.Count; i++)
        {
            var lot = lots[i % lots.Count];
            issues.Add(new MaterialIssue
            {
                JobId         = jobs[i].Id,
                PartId        = part.Id,
                Quantity      = 25m + (i * 10m),
                UnitCost      = 12.5m,
                IssuedById    = issuer.Value,
                IssuedAt      = now.AddDays(-i - 1),
                LotNumber     = lot.LotNumber,
                IssueType     = MaterialIssueType.Issue,
                Notes         = "RT-SEED lot consumption",
                CreatedAt     = now.AddDays(-i - 1),
                UpdatedAt     = now.AddDays(-i - 1),
            });
        }
        _db.MaterialIssues.AddRange(issues);
        await _db.SaveChangesAsync(ct);
        return lots.Count + issues.Count;
    }

    // ── finished-serials ────────────────────────────────────────────────────
    private async Task<int> SeedFinishedSerialsAsync(CancellationToken ct)
    {
        var existing = await _db.SerialNumbers
            .CountAsync(s => s.Notes != null && s.Notes.StartsWith(SentinelPrefix), ct);
        if (existing > 0) return 0;

        var part = await _db.Parts.OrderBy(p => p.Id).FirstOrDefaultAsync(ct);
        if (part == null) { Log.Information("Seed: no Parts — skipping finished-serials"); return 0; }

        var jobs = await _db.Jobs
            .Where(j => j.JobNumber.StartsWith(SentinelPrefix))
            .OrderBy(j => j.Id)
            .Take(2)
            .ToListAsync(ct);
        if (jobs.Count == 0)
            jobs = await _db.Jobs.OrderBy(j => j.Id).Take(2).ToListAsync(ct);
        if (jobs.Count == 0) { Log.Information("Seed: no Jobs — skipping finished-serials"); return 0; }

        // Re-use sentinel lots if present so serial → lot genealogy chains exist.
        var lots = await _db.LotRecords
            .Where(l => l.LotNumber.StartsWith(SentinelPrefix))
            .OrderBy(l => l.Id)
            .ToListAsync(ct);

        var now = _clock.UtcNow;
        var serials = new List<SerialNumber>();
        foreach (var job in jobs)
        {
            for (int n = 0; n < 4; n++)
            {
                var lot = lots.Count > 0 ? lots[(serials.Count) % lots.Count] : null;
                serials.Add(new SerialNumber
                {
                    PartId          = part.Id,
                    SerialValue     = $"{SentinelPrefix}SN-{job.Id:000}-{n + 1:00}",
                    Status          = SerialNumberStatus.Available,
                    JobId           = job.Id,
                    LotRecordId     = lot?.Id,
                    ManufacturedAt  = job.CompletedDate ?? now,
                    Notes           = $"{SentinelPrefix}finished serial — genealogy test",
                    CreatedAt       = now,
                    UpdatedAt       = now,
                });
            }
        }
        _db.SerialNumbers.AddRange(serials);
        await _db.SaveChangesAsync(ct);
        return serials.Count;
    }
}
