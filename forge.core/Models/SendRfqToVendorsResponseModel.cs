namespace Forge.Core.Models;

public record SendRfqToVendorsResponseModel(
    int RfqId,
    List<int> EmailedVendorIds,
    List<RfqVendorWithoutEmailModel> VendorsWithoutEmail,
    List<int> AlreadyInvitedVendorIds);
