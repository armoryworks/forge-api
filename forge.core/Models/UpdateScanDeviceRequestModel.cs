namespace Forge.Core.Models;

public record UpdateScanDeviceRequestModel(string? DeviceName, int? AssignedUserId, bool IsActive = true);
