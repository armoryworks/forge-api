using System.Globalization;
using System.Text.Json;

using MediatR;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

using Forge.Api.Capabilities;
using Forge.Api.Features.Accounting;
using Forge.Api.Features.Notifications;
using Forge.Core.Entities;
using Forge.Core.Enums.Accounting;
using Forge.Core.Interfaces;
using Forge.Core.Models;
using Forge.Core.Settings;
using Forge.Data.Context;

namespace Forge.Api.Jobs;

/// <summary>
/// §10.6 — the variance watchdog. The annual cost roll (plus gated ad-hoc re-rolls) is supported by this
/// daily job that surfaces variance drift in a friendly way instead of someone reading the variance
/// report every morning. For the active book's current open fiscal period it nets each standard-cost
/// variance account (via <see cref="IVarianceReportService"/>) plus the period's COGS, and when an
/// account's |net| exceeds <c>max(absolute floor, percent × |COGS|)</c> (floor only when COGS is zero)
/// it notifies every user in the Controller role with a plain-English nudge — material variances suggest
/// considering a standard-cost roll, labor/overhead ones suggest reviewing rates/routings.
/// <para>
/// Self-gated on CAP-ACCT-FULLGL (silent no-op while off). De-duped to at most ONE notification per
/// (variance account, fiscal period) via a compact-JSON state row in <c>system_settings</c>
/// (<c>{periodId}:{key}</c> entries, pruned to the current period) — re-runs never re-notify; a new
/// period re-arms every account.
/// </para>
/// </summary>
public class VarianceWatchdogJob(
    AppDbContext db,
    ICapabilitySnapshotProvider capabilities,
    IVarianceReportService varianceReport,
    ISettingsService settings,
    UserManager<ApplicationUser> userManager,
    IMediator mediator,
    IClock clock,
    ILogger<VarianceWatchdogJob> logger)
{
    /// <summary>The role whose members receive the watchdog's notifications.</summary>
    public const string ControllerRoleName = "Controller";

    /// <summary>Raw system_settings row holding the dedupe state (job state — not in the descriptor catalog).</summary>
    public const string NotifiedStateKey = "accounting.variance-watchdog.notified";

    private const string FullGlCapability = "CAP-ACCT-FULLGL";
    private const string CogsKey = "COGS";

    /// <summary>Variance keys whose drift suggests a standard-cost roll (material-side).</summary>
    private static readonly HashSet<string> MaterialVarianceKeys = new(StringComparer.Ordinal)
    {
        "PURCHASE_PRICE_VARIANCE",
        "MATERIAL_USAGE_VARIANCE",
    };

    /// <summary>Human phrasing per variance determination key (sentence-initial).</summary>
    private static readonly Dictionary<string, string> HumanNames = new(StringComparer.Ordinal)
    {
        ["PURCHASE_PRICE_VARIANCE"] = "Purchase price variance",
        ["MATERIAL_USAGE_VARIANCE"] = "Material usage variance",
        ["PRODUCTION_VARIANCE"] = "Production variance",
        ["LABOR_RATE_VARIANCE"] = "Labor rate variance",
        ["LABOR_EFFICIENCY_VARIANCE"] = "Labor efficiency variance",
        ["OVERHEAD_SPENDING_VARIANCE"] = "Overhead spending variance",
        ["OVERHEAD_EFFICIENCY_VARIANCE"] = "Overhead efficiency variance",
    };

    public async Task RunAsync(CancellationToken ct)
    {
        // Self-gate: the watchdog reads acct_* tables that only fill while the full GL is on.
        if (!capabilities.IsEnabled(FullGlCapability))
            return;

        var book = await db.Books.AsNoTracking()
            .Where(b => b.IsActive).OrderBy(b => b.Id).FirstOrDefaultAsync(ct);
        if (book is null)
        {
            logger.LogDebug("VarianceWatchdogJob: no active book — nothing to watch");
            return;
        }

        var today = DateOnly.FromDateTime(clock.UtcNow.UtcDateTime);
        var period = await db.FiscalPeriods.AsNoTracking()
            .Where(p => p.FiscalYear.BookId == book.Id
                && p.Status == FiscalPeriodStatus.Open
                && p.StartDate <= today && p.EndDate >= today)
            .OrderBy(p => p.StartDate)
            .FirstOrDefaultAsync(ct);
        if (period is null)
        {
            logger.LogDebug("VarianceWatchdogJob: no open fiscal period covers {Today} — skipping", today);
            return;
        }

        var floor = await GetDecimalSettingAsync(
            VarianceWatchdogSettings.AbsoluteFloorKey, VarianceWatchdogSettings.DefaultAbsoluteFloor, ct);
        var percent = await GetDecimalSettingAsync(
            VarianceWatchdogSettings.PercentOfCogsKey, VarianceWatchdogSettings.DefaultPercentOfCogs, ct);

        var report = await varianceReport.GetAsync(book.Id, period.StartDate, period.EndDate, ct);
        var cogs = await GetCogsNetAsync(book.Id, period.StartDate, period.EndDate, ct);

        // When COGS is 0 the percent leg contributes 0 and max() leaves the floor alone.
        var threshold = Math.Max(floor, percent / 100m * Math.Abs(cogs));
        var triggered = report.Lines.Where(l => Math.Abs(l.Amount) > threshold).ToList();

        // Dedupe state: "{periodId}:{key}" entries, pruned to the current period so the row stays compact.
        var stateRow = await db.SystemSettings.FirstOrDefaultAsync(s => s.Key == NotifiedStateKey, ct);
        var previous = ParseState(stateRow?.Value);
        var prefix = $"{period.Id}:";
        var state = previous.Where(e => e.StartsWith(prefix, StringComparison.Ordinal))
            .ToHashSet(StringComparer.Ordinal);

        var newTriggers = triggered.Where(l => !state.Contains($"{period.Id}:{l.Key}")).ToList();
        if (newTriggers.Count == 0)
        {
            await PersistStateAsync(stateRow, previous, state, ct);
            return;
        }

        var controllers = await userManager.GetUsersInRoleAsync(ControllerRoleName);
        if (controllers.Count == 0)
        {
            // Don't mark anything notified — once a Controller exists the nudge should still arrive.
            logger.LogWarning(
                "VarianceWatchdogJob: {Count} variance account(s) over threshold but no users hold the "
                + "Controller role — notifications skipped", newTriggers.Count);
            await PersistStateAsync(stateRow, previous, state, ct);
            return;
        }

        foreach (var line in newTriggers)
        {
            var message = BuildMessage(line.Key, line.Amount, cogs);

            foreach (var user in controllers)
            {
                try
                {
                    await mediator.Send(new CreateNotificationCommand(new CreateNotificationRequestModel(
                        UserId: user.Id,
                        Type: "alert",
                        Severity: "warning",
                        Source: "variance-watchdog",
                        Title: "Variance review suggested",
                        Message: message,
                        EntityType: null,
                        EntityId: null,
                        SenderId: null)), ct);
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    logger.LogError(ex,
                        "VarianceWatchdogJob: failed to notify user {UserId} about {VarianceKey}",
                        user.Id, line.Key);
                }
            }

            state.Add($"{period.Id}:{line.Key}");
        }

        await PersistStateAsync(stateRow, previous, state, ct);

        logger.LogInformation(
            "VarianceWatchdogJob: nudged {UserCount} Controller user(s) about {AccountCount} variance "
            + "account(s) over threshold {Threshold} for period {PeriodId}",
            controllers.Count, newTriggers.Count, threshold, period.Id);
    }

    /// <summary>Friendly copy: account in human terms + amount + % of period COGS + a tailored suggestion.</summary>
    private static string BuildMessage(string key, decimal amount, decimal cogs)
    {
        var name = HumanNames.TryGetValue(key, out var n) ? n : key;
        var magnitude = Math.Abs(amount).ToString("C", CultureInfo.GetCultureInfo("en-US"));

        var reached = cogs != 0m
            ? $"{name} has reached {magnitude} ({Math.Abs(amount) / Math.Abs(cogs) * 100m:0.0}% of this period's COGS)."
            : $"{name} has reached {magnitude} this period (no COGS posted yet, so the absolute threshold applies).";

        var suggestion = MaterialVarianceKeys.Contains(key)
            ? "Worth a look — if material costs have shifted durably, consider a standard-cost roll."
            : "Worth a look — review labor/overhead rates and routings; if the standards have drifted, "
              + "a re-roll may be in order.";

        return $"{reached} {suggestion}";
    }

    private async Task<decimal> GetDecimalSettingAsync(string key, decimal fallback, CancellationToken ct)
    {
        var raw = await settings.GetStringAsync(key, ct);
        return decimal.TryParse(raw, NumberStyles.Number, CultureInfo.InvariantCulture, out var value) && value >= 0m
            ? value
            : fallback;
    }

    /// <summary>Period COGS net (debit − credit) via the COGS determination key — mirrors the report's summing.</summary>
    private async Task<decimal> GetCogsNetAsync(int bookId, DateOnly from, DateOnly to, CancellationToken ct)
    {
        var cogsAccountId = await db.AccountDeterminationRules.AsNoTracking()
            .Where(r => r.BookId == bookId && r.Key == CogsKey)
            .Select(r => (int?)r.GlAccountId)
            .FirstOrDefaultAsync(ct);
        if (cogsAccountId is null)
            return 0m;

        var lo = from; // `from`/`to` collide with LINQ query keywords inside the expression below
        var hi = to;
        return await
            (from line in db.JournalLines.IgnoreQueryFilters()
             join je in db.JournalEntries.IgnoreQueryFilters() on line.JournalEntryId equals je.Id
             where je.BookId == bookId
                 && line.GlAccountId == cogsAccountId
                 && je.EntryDate >= lo && je.EntryDate <= hi
                 && (je.Status == JournalEntryStatus.Posted || je.Status == JournalEntryStatus.Reversed)
             select line.Debit - line.Credit)
            .SumAsync(ct);
    }

    private static HashSet<string> ParseState(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
            return new HashSet<string>(StringComparer.Ordinal);
        try
        {
            return JsonSerializer.Deserialize<List<string>>(json)?.ToHashSet(StringComparer.Ordinal)
                ?? new HashSet<string>(StringComparer.Ordinal);
        }
        catch (JsonException)
        {
            return new HashSet<string>(StringComparer.Ordinal); // corrupt state self-heals on the next write
        }
    }

    /// <summary>Upserts the dedupe row when its content changed (covers both pruning and new entries).</summary>
    private async Task PersistStateAsync(
        SystemSetting? row, HashSet<string> previous, HashSet<string> current, CancellationToken ct)
    {
        if (previous.SetEquals(current))
            return;

        var value = JsonSerializer.Serialize(current.Order(StringComparer.Ordinal).ToList());
        if (row is null)
        {
            db.SystemSettings.Add(new SystemSetting
            {
                Key = NotifiedStateKey,
                Value = value,
                Description = "Variance-watchdog dedupe state — \"{periodId}:{varianceKey}\" entries already notified.",
            });
        }
        else
        {
            row.Value = value;
        }
        await db.SaveChangesAsync(ct);
    }
}
