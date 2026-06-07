using Forge.Core.Enums.Accounting;
using Forge.Core.Models.Accounting;

namespace Forge.Core.Interfaces;

/// <summary>
/// ⚡ Phase-3 — fiscal-period close/reopen seam. Transitions a <c>FiscalPeriod</c> through its lifecycle
/// (Open → SoftClosed → HardClosed, and SoftClosed → Open to reopen), taking a row lock so a concurrent post
/// blocks and observes the new status (the engine locks the same row when resolving a post's period). The
/// engine already enforces the status on posting (SoftClosed blocks unless an audited override; HardClosed
/// rejects outright), so closing is purely the status transition.
/// </summary>
public interface IFiscalPeriodCloseService
{
    /// <summary>
    /// Transitions a period to <paramref name="target"/>; throws on an illegal transition. Stamps the
    /// close/reopen audit (<paramref name="actorUserId"/> + timestamp).
    /// </summary>
    Task<FiscalPeriodModel> TransitionAsync(
        int periodId, FiscalPeriodStatus target, int actorUserId, CancellationToken ct = default);
}
