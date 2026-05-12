namespace Forge.Core.Models;

public record BoardJobUpdatedEvent(
    int JobId,
    JobDetailResponseModel Job);
