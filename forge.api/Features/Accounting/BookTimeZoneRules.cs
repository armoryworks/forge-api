using Microsoft.EntityFrameworkCore;

using Forge.Core.Enums.Accounting;
using Forge.Data.Context;

namespace Forge.Api.Features.Accounting;

/// <summary>
/// The company time zone (<c>company.time_zone</c>, Admin &gt; Company profile) and the per-book reporting
/// time zone that anchors entry-date to period resolution. A book's zone is fixed once the book has a posted
/// entry (industry practice: the fiscal calendar does not move under posted history).
/// </summary>
public static class BookTimeZoneRules
{
    public const string CompanyTimeZoneKey = "company.time_zone";
    public const string FallbackTimeZone = "America/New_York";
    public const string LockedCode = "book-timezone-locked";

    /// <summary>True when <paramref name="timeZoneId"/> names a zone this server knows (IANA or Windows id).</summary>
    public static bool IsKnownTimeZone(string? timeZoneId)
        => !string.IsNullOrWhiteSpace(timeZoneId)
            && TimeZoneInfo.TryFindSystemTimeZoneById(timeZoneId.Trim(), out _);

    /// <summary>The company time zone when it is set and known; otherwise null.</summary>
    public static async Task<string?> CompanyTimeZoneAsync(AppDbContext db, CancellationToken ct = default)
    {
        var value = await db.SystemSettings.AsNoTracking()
            .Where(s => s.Key == CompanyTimeZoneKey)
            .Select(s => s.Value)
            .FirstOrDefaultAsync(ct);
        return IsKnownTimeZone(value) ? value!.Trim() : null;
    }

    /// <summary>True once the book carries any posted (or posted-then-reversed) journal entry.</summary>
    public static Task<bool> HasPostedEntriesAsync(AppDbContext db, int bookId, CancellationToken ct = default)
        => db.JournalEntries.IgnoreQueryFilters().AsNoTracking()
            .AnyAsync(e => e.BookId == bookId
                && (e.Status == JournalEntryStatus.Posted || e.Status == JournalEntryStatus.Reversed), ct);
}
