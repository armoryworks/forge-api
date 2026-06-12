namespace Forge.Core.Models;

/// <summary>
/// ⚡ BANKING BOUNDARY — create/update payload for a vendor bank account. The plaintext numbers
/// exist transiently in this request only; the handler validates (ABA checksum), encrypts, masks,
/// and discards them. Any change to the numbers resets the account to PendingApproval (dual control).
/// </summary>
public record SaveVendorBankAccountRequestModel(
    string Nickname,
    string AccountType,
    string RoutingNumber,
    string AccountNumber);
