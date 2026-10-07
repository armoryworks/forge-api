using Forge.Api.Capabilities;
using Forge.Core.Enums;

namespace Forge.Api.Features.Inventory;

public static class ReceivingInspectionPolicy
{
    public const string Capability = "CAP-QC-INSPECTION";

    public static ReceivingInspectionStatus InitialStatus(bool partRequiresInspection, ICapabilitySnapshotProvider? capabilities)
        => partRequiresInspection && capabilities?.IsEnabled(Capability) == true
            ? ReceivingInspectionStatus.Pending
            : ReceivingInspectionStatus.NotRequired;
}
