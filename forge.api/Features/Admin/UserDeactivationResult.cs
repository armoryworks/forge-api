namespace Forge.Api.Features.Admin;

public record UserDeactivationResult(int UnassignedJobCount, bool StoppedTimer, bool ClockedOut);
