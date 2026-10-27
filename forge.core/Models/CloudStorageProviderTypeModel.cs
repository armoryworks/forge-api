namespace Forge.Core.Models;

public record CloudStorageProviderTypeModel(
    string Code,
    string? CapabilityCode,
    bool CapabilityEnabled,
    bool Registered);
