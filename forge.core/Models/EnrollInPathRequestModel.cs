namespace Forge.Core.Models;

public record EnrollInPathRequestModel(
    IReadOnlyList<int>? UserIds,
    IReadOnlyList<string>? Roles,
    DateOnly? DueDate);
