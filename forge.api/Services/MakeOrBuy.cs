using Forge.Core.Entities;
using Forge.Core.Enums;

namespace Forge.Api.Services;

public static class MakeOrBuy
{
    public static bool PlansAsMake(ProcurementSource source, bool hasRoutingOrBom, bool hasVendorSource) =>
        source == ProcurementSource.Make
        || (source == ProcurementSource.Buy && hasRoutingOrBom && !hasVendorSource);

    public static bool HasTimeStandards(IEnumerable<Operation> routing) =>
        routing.Any(op => OperationTimeMath.PlannedMinutes(op, 1m) > 0m);
}
