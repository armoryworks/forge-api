namespace Forge.Core.Models;

public record LaborRecomputeRequestModel(DateOnly From, DateOnly To, int? UserId = null, bool Preview = true);
