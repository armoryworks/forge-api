using System.Globalization;

using Forge.Core.Entities;

namespace Forge.Api.Features.Events;

public static class EventTimeText
{
    public const string DateFormat = "MM/dd/yyyy";
    public const string DateTimeFormat = "MM/dd/yyyy hh:mm tt";

    public static string Start(Event evt, TimeZoneInfo shopZone) =>
        evt.IsAllDay
            ? evt.StartTime.UtcDateTime.ToString(DateFormat, CultureInfo.InvariantCulture)
            : Instant(evt.StartTime, shopZone);

    public static string Instant(DateTimeOffset value, TimeZoneInfo shopZone) =>
        TimeZoneInfo.ConvertTime(value, shopZone).ToString(DateTimeFormat, CultureInfo.InvariantCulture);
}
