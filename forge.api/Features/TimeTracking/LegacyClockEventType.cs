using Forge.Core.Enums;
using Forge.Core.Interfaces;

namespace Forge.Api.Features.TimeTracking;

public static class LegacyClockEventType
{
    public static ClockEventType From(ClockEventTypeDefinition definition)
    {
        if (Enum.TryParse<ClockEventType>(definition.Code, out var parsed) && Enum.IsDefined(parsed))
            return parsed;

        return definition.StatusMapping switch
        {
            "In" => ClockEventType.ClockIn,
            "Out" => ClockEventType.ClockOut,
            _ => ClockEventType.BreakStart,
        };
    }
}
