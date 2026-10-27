namespace Forge.Core.Models;

public record ReviewCycleProgressModel(
    int NotStarted,
    int SelfSubmitted,
    int ManagerSubmitted,
    int Acknowledged,
    int Closed);
