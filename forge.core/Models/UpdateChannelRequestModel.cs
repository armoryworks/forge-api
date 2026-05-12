namespace Forge.Core.Models;

public record UpdateChannelRequestModel(
    string? Name,
    string? Description,
    string? IconName);
