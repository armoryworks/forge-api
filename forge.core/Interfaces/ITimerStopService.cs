using Forge.Core.Entities;
using Forge.Core.Models;

namespace Forge.Core.Interfaces;

public interface ITimerStopService
{
    void Close(TimeEntry entry, DateTimeOffset stopAt, string? notes = null, string? reason = null, string? appendNote = null);

    Task<IReadOnlyList<TimeEntryResponseModel>> PublishStoppedAsync(
        IReadOnlyList<TimeEntry> entries, CancellationToken cancellationToken);
}
