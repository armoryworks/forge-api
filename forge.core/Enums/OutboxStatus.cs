namespace Forge.Core.Enums;

public enum OutboxStatus
{
    Pending,
    InFlight,
    Sent,
    Failed,
    DeadLetter,
}
