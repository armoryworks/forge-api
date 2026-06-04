using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Diagnostics;

using Forge.Core.Entities.Accounting;
using Forge.Core.Enums.Accounting;

namespace Forge.Data.Interceptors;

/// <summary>
/// Enforces append-only immutability on posted ledger rows in software (§2, §4,
/// §5.2). A companion Postgres <c>BEFORE UPDATE/DELETE</c> trigger enforces the
/// same invariant at the database (defense in depth) — this interceptor is the
/// first, friendlier line.
/// <para>
/// Rules, applied to <see cref="JournalEntry"/> and <see cref="JournalLine"/>:
/// </para>
/// <list type="bullet">
///   <item>A <c>JournalLine</c> belonging to a Posted/Reversed entry may never
///   be <c>Modified</c> or <c>Deleted</c>.</item>
///   <item>A Posted <c>JournalEntry</c> may never be <c>Deleted</c>.</item>
///   <item>The ONLY mutation permitted on a Posted <c>JournalEntry</c> is the
///   single <c>Posted→Reversed</c> status flip plus the
///   <see cref="JournalEntry.ReversedByEntryId"/> link (written by
///   <c>ReverseAsync</c>) — every other modified property is rejected.</item>
/// </list>
/// <para>
/// Draft / PendingApproval / Approved entries remain freely mutable by their
/// author until posted; only <c>Posted</c> is locked down.
/// </para>
/// </summary>
public sealed class LedgerImmutabilityInterceptor : SaveChangesInterceptor
{
    public override InterceptionResult<int> SavingChanges(
        DbContextEventData eventData, InterceptionResult<int> result)
    {
        Enforce(eventData.Context);
        return base.SavingChanges(eventData, result);
    }

    public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
        DbContextEventData eventData,
        InterceptionResult<int> result,
        CancellationToken cancellationToken = default)
    {
        Enforce(eventData.Context);
        return base.SavingChangesAsync(eventData, result, cancellationToken);
    }

    private static void Enforce(DbContext? context)
    {
        if (context is null)
            return;

        foreach (var entry in context.ChangeTracker.Entries<JournalLine>())
        {
            if (entry.State is not (EntityState.Modified or EntityState.Deleted))
                continue;

            // A line's lock follows its header: once the header is Posted (or
            // Reversed), its lines are frozen. Reversals add NEW lines (Added),
            // which are always allowed.
            var status = ResolveOwningEntryStatus(entry);
            if (status is JournalEntryStatus.Posted or JournalEntryStatus.Reversed)
            {
                throw new InvalidOperationException(
                    $"Ledger immutability violation: journal line {entry.Entity.Id} on a " +
                    $"{status} entry cannot be {entry.State}. Corrections are made via reversing entries only.");
            }
        }

        foreach (var entry in context.ChangeTracker.Entries<JournalEntry>())
        {
            switch (entry.State)
            {
                case EntityState.Deleted when IsLocked(entry.Entity, entry):
                    throw new InvalidOperationException(
                        $"Ledger immutability violation: posted journal entry {entry.Entity.Id} cannot be deleted. " +
                        "Corrections are made via reversing entries only.");

                case EntityState.Modified when WasPosted(entry):
                    if (!IsPermittedReversalFlip(entry))
                    {
                        var changed = string.Join(", ", entry.Properties
                            .Where(p => p.IsModified)
                            .Select(p => p.Metadata.Name));
                        throw new InvalidOperationException(
                            $"Ledger immutability violation: posted journal entry {entry.Entity.Id} is append-only. " +
                            $"The only permitted mutation is the Posted→Reversed flip + ReversedByEntryId link; " +
                            $"attempted to modify [{changed}].");
                    }
                    break;
            }
        }
    }

    private static bool IsLocked(JournalEntry e, EntityEntry<JournalEntry> entry)
        => WasPosted(entry) || e.Status is JournalEntryStatus.Posted or JournalEntryStatus.Reversed;

    /// <summary>
    /// True when the row's ORIGINAL (database) status was Posted — i.e. it was
    /// already locked before this save. Uses original values so an in-flight
    /// Posted→Reversed flip is detected by its prior state, not its new one.
    /// </summary>
    private static bool WasPosted(EntityEntry<JournalEntry> entry)
    {
        var original = entry.OriginalValues;
        var statusProp = entry.Property(e => e.Status);
        // For Added-then-Modified-in-same-context edge cases OriginalValues may
        // equal current; rely on the original status value.
        return (JournalEntryStatus)original[statusProp.Metadata]! == JournalEntryStatus.Posted;
    }

    /// <summary>
    /// True when the only changes on a previously-Posted entry are the
    /// Status flip to Reversed (+ optional ReversedByEntryId link). Any other
    /// modified property fails the carve-out.
    /// </summary>
    private static bool IsPermittedReversalFlip(EntityEntry<JournalEntry> entry)
    {
        // The new status must be exactly Reversed.
        if (entry.Entity.Status != JournalEntryStatus.Reversed)
            return false;

        foreach (var prop in entry.Properties)
        {
            if (!prop.IsModified)
                continue;

            var name = prop.Metadata.Name;
            if (name is nameof(JournalEntry.Status) or nameof(JournalEntry.ReversedByEntryId))
                continue;

            // Any other modified property breaks the carve-out.
            return false;
        }

        return true;
    }

    private static JournalEntryStatus? ResolveOwningEntryStatus(EntityEntry<JournalLine> lineEntry)
    {
        // Prefer the tracked navigation if loaded.
        var owner = lineEntry.Entity.JournalEntry;
        if (owner is not null)
            return owner.Status;

        // Otherwise find the tracked header by FK in the same context.
        var ctx = lineEntry.Context;
        var headerId = lineEntry.Entity.JournalEntryId;
        var tracked = ctx.ChangeTracker.Entries<JournalEntry>()
            .FirstOrDefault(e => e.Entity.Id == headerId);
        return tracked?.Entity.Status;
    }
}
