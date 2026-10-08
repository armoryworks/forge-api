using Forge.Core.Enums;

namespace Forge.Api.Services;

public static class MakeOrBuy
{
    public static bool PlansAsMake(ProcurementSource source, bool hasRouting, bool hasVendorSource) =>
        source == ProcurementSource.Make
        || (source == ProcurementSource.Buy && hasRouting && !hasVendorSource);
}
