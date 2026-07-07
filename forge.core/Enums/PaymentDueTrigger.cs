namespace Forge.Core.Enums;

public enum PaymentDueTrigger
{
    OnAcceptance,
    OnOrderConfirmation,
    OnProductionStart,
    OnShipment,
    OnDelivery,
    FixedDate,
    NetDays,
}
