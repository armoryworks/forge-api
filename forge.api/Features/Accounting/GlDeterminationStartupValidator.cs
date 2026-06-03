using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

using Forge.Core.Interfaces;
using Forge.Data.Context;

namespace Forge.Api.Features.Accounting;

/// <summary>
/// Startup validator for the account-determination map (ACCOUNTING_SUITE_PLAN
/// §5.2: "determination targets validated at seed time AND on startup — every
/// key→account resolves, is postable, in-book — so misconfiguration is caught
/// before the first posting, not on the hot path").
/// <para>
/// For each active book it asks <see cref="IAccountDeterminationResolver.ValidateKeysAsync"/>
/// to resolve every key that has a determination rule, collecting the keys whose
/// target is unmapped / non-postable / inactive / cross-book.
/// </para>
/// <para>
/// <b>Severity follows CAP-ACCT-FULLGL.</b> Since the capability is OFF in
/// Phase 0 (the GL is dark — nothing posts), failures are logged as a
/// <b>warning</b> and startup proceeds. When FULLGL is ON for the install
/// (Phase 1+), a misconfigured determination map would break live postings, so
/// the validator <b>fails fast</b> by throwing.
/// </para>
/// </summary>
public sealed class GlDeterminationStartupValidator(
    AppDbContext db,
    IAccountDeterminationResolver resolver,
    ILogger<GlDeterminationStartupValidator> logger)
{
    /// <param name="fullGlEnabled">
    /// Current CAP-ACCT-FULLGL state. True → fail fast on any unresolved key;
    /// false (Phase-0 dark default) → warn and continue.
    /// </param>
    public async Task ValidateAsync(bool fullGlEnabled, CancellationToken ct = default)
    {
        var books = await db.Books.AsNoTracking()
            .Where(b => b.IsActive)
            .Select(b => new { b.Id, b.Code })
            .ToListAsync(ct);

        if (books.Count == 0)
        {
            logger.LogDebug("[GL-DETERMINATION] No books present; skipping determination validation.");
            return;
        }

        var anyFailures = false;

        foreach (var book in books)
        {
            // The keys to validate are exactly those that have a rule in this
            // book — asserting each configured rule points at a postable, active,
            // in-book account. (An entirely unmapped key only matters when a
            // posting references it; that is enforced on the hot path.)
            var keys = await db.AccountDeterminationRules.AsNoTracking()
                .Where(r => r.BookId == book.Id)
                .Select(r => r.Key)
                .Distinct()
                .ToListAsync(ct);

            if (keys.Count == 0)
            {
                logger.LogDebug("[GL-DETERMINATION] Book {Book} has no determination rules.", book.Code);
                continue;
            }

            var failed = await resolver.ValidateKeysAsync(book.Id, keys, ct);
            if (failed.Count == 0)
            {
                logger.LogInformation(
                    "[GL-DETERMINATION] Book {Book}: all {Count} determination keys resolve to postable, in-book accounts.",
                    book.Code, keys.Count);
                continue;
            }

            anyFailures = true;
            logger.LogWarning(
                "[GL-DETERMINATION] Book {Book}: {FailCount}/{Total} determination keys do NOT resolve to a postable, " +
                "active, in-book account: {Keys}.",
                book.Code, failed.Count, keys.Count, string.Join(", ", failed));
        }

        if (anyFailures && fullGlEnabled)
            throw new InvalidOperationException(
                "CAP-ACCT-FULLGL is enabled but one or more account-determination keys do not resolve to a postable, " +
                "active, in-book account. Fix the determination map before serving requests (ACCOUNTING_SUITE_PLAN §5.2).");

        if (anyFailures)
            logger.LogWarning(
                "[GL-DETERMINATION] Determination map has unresolved keys, but CAP-ACCT-FULLGL is OFF (GL is dark) — " +
                "continuing startup. These would fail fast once FULLGL is enabled.");
    }
}
