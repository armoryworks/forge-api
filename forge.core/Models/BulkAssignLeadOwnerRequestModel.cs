namespace Forge.Core.Models;

public record BulkAssignLeadOwnerRequestModel(List<int> LeadIds, int AssignedToUserId);

public record BulkAssignLeadOwnerResponseModel(int Updated);
