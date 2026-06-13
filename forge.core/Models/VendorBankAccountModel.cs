namespace Forge.Core.Models;

/// <summary>
/// ⚡ BANKING BOUNDARY — display projection of a vendor bank account. Carries ONLY the masked
/// number twins; the encrypted values never leave the server (decrypted solely inside NACHA
/// file generation).
/// </summary>
public record VendorBankAccountModel(
    int Id,
    int VendorId,
    string VendorName,
    string Nickname,
    string AccountType,
    string RoutingNumberMasked,
    string AccountNumberMasked,
    string Status,
    int ChangedByUserId,
    int? ApprovedByUserId,
    DateTimeOffset? ApprovedAt,
    DateTimeOffset? PrenoteSentAt,
    DateTimeOffset? VerifiedAt,
    DateTimeOffset CreatedAt);
