using Forge.Core.Enums;

namespace Forge.Core.Models;

public record DisposeJobRequestModel(
    JobDisposition Disposition,
    string? Notes);
