using Forge.Core.Interfaces;

namespace Forge.Api.Features.TimeTracking;

public static class ClockEventActivity
{
    public static string Describe(ClockEventTypeDefinition definition, string? source, StoppedTimerResponseModel? stopped)
    {
        var description = string.IsNullOrWhiteSpace(source)
            ? definition.Label
            : $"{definition.Label} via {source}";

        if (stopped is not null)
            description += $"; stopped timer on {stopped.JobNumber ?? "general time"}";

        return description;
    }
}
