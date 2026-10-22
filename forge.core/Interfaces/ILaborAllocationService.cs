using Forge.Core.Entities;
using Forge.Core.Models;

namespace Forge.Core.Interfaces;

public interface ILaborAllocationService
{
    /// <summary>Writes the labor share of entries that were just stopped together (one batch), sharing the person's
    /// clocked time with the other timers they had open. Locked entries are left alone. No save.</summary>
    Task AllocateStoppedAsync(IReadOnlyList<TimeEntry> entries, CancellationToken ct);

    /// <summary>Re-runs the split for the person's stopped timers that overlap the given spans (an entry was edited,
    /// deleted or corrected, or a clock event was undone) and re-prices the entries whose share changed. Refused when
    /// it would change a locked entry. <paramref name="edited"/> are tracked entries carrying unsaved changes. No save.</summary>
    Task ReallocateAsync(
        int userId,
        IReadOnlyList<(DateTimeOffset Start, DateTimeOffset End)> spans,
        IReadOnlyList<TimeEntry> edited,
        CancellationToken ct);

    /// <summary>Re-runs the split over the unlocked stopped timers dated in a range and reports the labor and cost
    /// change per job. Applies and saves it when <paramref name="apply"/> is true.</summary>
    Task<LaborRecomputeResponseModel> RecomputeAsync(int? userId, DateOnly from, DateOnly to, bool apply, CancellationToken ct);
}
