namespace Forge.Core.Models;

public record MrpPartPlanResponseModel(
    int PartId,
    string PartNumber,
    string PartDescription,
    List<MrpTimeBucket> Buckets
);

public record MrpTimeBucket(
    DateTimeOffset PeriodStart,
    DateTimeOffset PeriodEnd,
    decimal GrossRequirements,
    decimal ScheduledReceipts,
    decimal PlannedOrderReceipts,
    decimal ProjectedOnHand,
    decimal NetRequirements,
    decimal PlannedOrderReleases
);
