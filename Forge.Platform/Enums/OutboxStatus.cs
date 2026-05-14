namespace Forge.Platform.Enums;

public enum OutboxStatus
{
    Pending,
    InFlight,
    Sent,
    Failed,
    DeadLetter,
}
