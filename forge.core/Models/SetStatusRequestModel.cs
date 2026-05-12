namespace Forge.Core.Models;

public record SetStatusRequestModel(
    string StatusCode,
    string? Notes);
