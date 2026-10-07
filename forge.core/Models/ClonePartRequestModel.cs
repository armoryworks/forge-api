namespace Forge.Core.Models;

public record ClonePartRequestModel(
    string Name,
    string? PartNumber = null,
    string? Description = null,
    bool CopyBom = true,
    bool CopyRouting = true,
    bool CopyVendorSources = false);
