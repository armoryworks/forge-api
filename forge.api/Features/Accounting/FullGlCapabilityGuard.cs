using Microsoft.EntityFrameworkCore;

using Forge.Data.Context;

namespace Forge.Api.Features.Accounting;

/// <summary>
/// One general-ledger rule a proposed capability change breaks.
/// </summary>
/// <param name="Code">Machine-readable violation code (<c>capability-gl-opening-balances</c> or <c>capability-gl-live-ledger</c>).</param>
/// <param name="Capability">The capability whose change is refused.</param>
/// <param name="BookId">The book that fails the rule, when the rule is per book.</param>
/// <param name="Message">Human-readable explanation.</param>
public sealed record FullGlCapabilityViolation(string Code, string Capability, int? BookId, string Message);

/// <summary>
/// The general-ledger rules that sit on top of the capability dependency graph, shared by
/// every path that changes capability state (single toggle, bulk toggle, preset apply,
/// discovery apply) and by their previews, so no path can bypass them.
/// <list type="bullet">
/// <item>Opening-balances hard-gate (§5.5 / §7A): CAP-ACCT-FULLGL cannot be turned on while
/// any active book lacks its posted Conversion opening journal. Installs with no book pass.</item>
/// <item>Live-ledger guard (fullgl-deactivation §3.5): once a book's opening balances are
/// loaded, CAP-ACCT-FULLGL cannot be turned off by a capability change, and accounting cannot
/// be switched to CAP-ACCT-EXTERNAL, because stopping posting mid-period desyncs the
/// sub-ledgers from the general ledger. That needs the governed, dated deactivation cutover.</item>
/// <item>GL-VIEW cascade: turning CAP-ACCT-FULLGL on also turns CAP-ACCT-GL-VIEW on. GL-VIEW
/// is left on when FULLGL goes off, so a deactivated ledger stays viewable (§3.1).</item>
/// </list>
/// Rules are evaluated against the candidate state (current state with the proposed changes
/// applied), the same whole-set semantic the dependency checks use.
/// </summary>
public interface IFullGlCapabilityGuard
{
    /// <summary>
    /// True when CAP-ACCT-FULLGL is on in <paramref name="current"/> and at least one active
    /// book has its opening balances loaded.
    /// </summary>
    Task<bool> IsLedgerLiveAsync(IReadOnlyDictionary<string, bool> current, CancellationToken ct = default);

    /// <summary>
    /// Returns every general-ledger rule that moving from <paramref name="current"/> to
    /// <paramref name="candidate"/> breaks. Empty when the change is allowed.
    /// </summary>
    Task<IReadOnlyList<FullGlCapabilityViolation>> EvaluateAsync(
        IReadOnlyDictionary<string, bool> current,
        IReadOnlyDictionary<string, bool> candidate,
        CancellationToken ct = default);

    /// <summary>
    /// Turns CAP-ACCT-GL-VIEW on (creating its row when missing) and saves. Returns the row
    /// when it changed, or <c>null</c> when it was already on.
    /// </summary>
    Task<Capability?> EnableGlViewAsync(CancellationToken ct = default);
}

/// <inheritdoc />
public sealed class FullGlCapabilityGuard(AppDbContext db, IGlCapabilityGate glGate) : IFullGlCapabilityGuard
{
    /// <summary>The capability that posts to the native general ledger.</summary>
    private const string FullGl = "CAP-ACCT-FULLGL";

    /// <summary>The read-only ledger view surface.</summary>
    private const string GlView = "CAP-ACCT-GL-VIEW";

    /// <summary>External accounting mode, mutually exclusive with the built-in books.</summary>
    private const string External = "CAP-ACCT-EXTERNAL";

    public async Task<bool> IsLedgerLiveAsync(IReadOnlyDictionary<string, bool> current, CancellationToken ct = default)
    {
        if (!IsOn(current, FullGl)) return false;
        foreach (var bookId in await ActiveBookIdsAsync(ct))
        {
            if (await glGate.AreOpeningBalancesLoadedAsync(bookId, ct)) return true;
        }
        return false;
    }

    public async Task<IReadOnlyList<FullGlCapabilityViolation>> EvaluateAsync(
        IReadOnlyDictionary<string, bool> current,
        IReadOnlyDictionary<string, bool> candidate,
        CancellationToken ct = default)
    {
        var violations = new List<FullGlCapabilityViolation>();
        var fullGlFrom = IsOn(current, FullGl);
        var fullGlTo = IsOn(candidate, FullGl);
        var externalTurningOn = !IsOn(current, External) && IsOn(candidate, External);

        if (fullGlFrom == fullGlTo && !(fullGlFrom && externalTurningOn))
            return violations;

        foreach (var bookId in await ActiveBookIdsAsync(ct))
        {
            if (!fullGlFrom && fullGlTo)
            {
                var eligibility = await glGate.EvaluateAsync(bookId, ct);
                if (!eligibility.CanEnable)
                {
                    violations.Add(new FullGlCapabilityViolation(
                        "capability-gl-opening-balances",
                        FullGl,
                        bookId,
                        eligibility.Reason ?? $"Book {bookId} has not passed the opening-balances gate."));
                }
                continue;
            }

            if (!await glGate.AreOpeningBalancesLoadedAsync(bookId, ct)) continue;

            if (!fullGlTo)
            {
                violations.Add(new FullGlCapabilityViolation(
                    "capability-gl-live-ledger",
                    FullGl,
                    bookId,
                    $"CAP-ACCT-FULLGL cannot be turned off for book {bookId} by toggling the " +
                    "capability: the ledger is live (opening balances are loaded), so stopping " +
                    "posting mid-period would desync the sub-ledgers from the general ledger. Turn " +
                    "the ledger off through the governed deactivation cutover (close and tie out the " +
                    "period, then deactivate with a successor system)."));
            }
            else if (externalTurningOn)
            {
                violations.Add(new FullGlCapabilityViolation(
                    "capability-gl-live-ledger",
                    External,
                    bookId,
                    "Your general ledger is live, so accounting can't be switched to an external " +
                    "system by changing capabilities. Close and tie out the period, then turn the " +
                    "ledger off through the deactivation cutover before you switch."));
            }
        }

        return violations;
    }

    public async Task<Capability?> EnableGlViewAsync(CancellationToken ct = default)
    {
        var view = await db.Capabilities.FirstOrDefaultAsync(c => c.Code == GlView, ct);
        if (view is null)
        {
            view = new Capability
            {
                Code = GlView,
                Area = "ACCT",
                Name = "General-ledger read-only view",
                Description = "Read-only access to the ledger, statements and exports. Auto-enabled with full GL.",
                Enabled = true,
            };
            db.Capabilities.Add(view);
        }
        else if (view.Enabled)
        {
            return null;
        }
        else
        {
            view.Enabled = true;
        }

        await db.SaveChangesAsync(ct);
        return view;
    }

    private Task<List<int>> ActiveBookIdsAsync(CancellationToken ct) =>
        db.Books
            .AsNoTracking()
            .Where(b => b.IsActive)
            .Select(b => b.Id)
            .ToListAsync(ct);

    private static bool IsOn(IReadOnlyDictionary<string, bool> state, string code) =>
        state.TryGetValue(code, out var on) && on;
}
