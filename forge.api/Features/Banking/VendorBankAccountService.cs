using Microsoft.EntityFrameworkCore;

using Forge.Api.Services;
using Forge.Core.Entities;
using Forge.Core.Enums;
using Forge.Core.Interfaces;
using Forge.Core.Models;
using Forge.Core.Settings;
using Forge.Data.Context;
using Forge.Data.Extensions;

namespace Forge.Api.Features.Banking;

/// <summary>
/// ⚡ BANKING BOUNDARY — vendor bank account lifecycle under the BANK-002 controls:
/// <list type="bullet">
///   <item>Numbers validated (ABA checksum), encrypted (Forge.Banking purpose), and masked at the
///         door; the response models only ever carry the masks.</item>
///   <item><b>Dual control:</b> every create/change lands PendingApproval and must be approved by a
///         user OTHER than the change-maker. Changing a verified account resets it (re-approval +
///         re-prenote — the destination is materially new).</item>
///   <item><b>Prenote:</b> Approved accounts ride a zero-dollar prenote batch; release marks them
///         PrenoteSent; once the return window passes a user marks them Verified. When
///         banking.require-prenote is off, Approved is already payable.</item>
/// </list>
/// </summary>
public interface IVendorBankAccountService
{
    Task<IReadOnlyList<VendorBankAccountModel>> ListAsync(int? vendorId, string? status, CancellationToken ct = default);
    Task<VendorBankAccountModel> CreateAsync(int vendorId, SaveVendorBankAccountRequestModel request, int userId, CancellationToken ct = default);
    Task<VendorBankAccountModel> UpdateNumbersAsync(int accountId, SaveVendorBankAccountRequestModel request, int userId, CancellationToken ct = default);
    Task<VendorBankAccountModel> ApproveAsync(int accountId, int userId, CancellationToken ct = default);
    Task<VendorBankAccountModel> MarkVerifiedAsync(int accountId, int userId, CancellationToken ct = default);
    Task<VendorBankAccountModel> DisableAsync(int accountId, int userId, CancellationToken ct = default);

    /// <summary>
    /// The vendor's most recently approved PAYABLE account — Verified, or Approved when prenoting
    /// is disabled. Null when the vendor has none (payments can't batch).
    /// </summary>
    Task<VendorBankAccount?> ResolvePayableAccountAsync(int vendorId, CancellationToken ct = default);
}

/// <inheritdoc />
public sealed class VendorBankAccountService(
    AppDbContext db,
    IBankingDataProtector protector,
    ISettingsService settings,
    IClock clock) : IVendorBankAccountService
{
    public async Task<IReadOnlyList<VendorBankAccountModel>> ListAsync(
        int? vendorId, string? status, CancellationToken ct = default)
    {
        var query = db.VendorBankAccounts.AsNoTracking().Include(a => a.Vendor).AsQueryable();
        if (vendorId is int vid)
            query = query.Where(a => a.VendorId == vid);
        if (Enum.TryParse<VendorBankAccountStatus>(status, ignoreCase: true, out var st))
            query = query.Where(a => a.Status == st);

        var accounts = await query.OrderByDescending(a => a.Id).ToListAsync(ct);
        return accounts.Select(ToModel).ToList();
    }

    public async Task<VendorBankAccountModel> CreateAsync(
        int vendorId, SaveVendorBankAccountRequestModel request, int userId, CancellationToken ct = default)
    {
        var vendor = await db.Set<Vendor>().FirstOrDefaultAsync(v => v.Id == vendorId, ct)
            ?? throw new KeyNotFoundException($"Vendor {vendorId} not found");

        var (routing, accountNumber, accountType) = ValidateNumbers(request);

        var account = new VendorBankAccount
        {
            VendorId = vendorId,
            Nickname = request.Nickname.Trim(),
            AccountType = accountType,
            RoutingNumberEncrypted = protector.Protect(routing)!,
            AccountNumberEncrypted = protector.Protect(accountNumber)!,
            RoutingNumberMasked = Mask(routing),
            AccountNumberMasked = Mask(accountNumber),
            Status = VendorBankAccountStatus.PendingApproval,
            ChangedByUserId = userId,
        };
        db.VendorBankAccounts.Add(account);
        await db.SaveChangesAsync(ct);

        db.LogActivityAt(
            "bank-account-added",
            $"Bank account '{account.Nickname}' ({account.AccountNumberMasked}) added for {vendor.CompanyName} — pending dual-control approval",
            ("Vendor", vendorId));
        await db.SaveChangesAsync(ct);

        account.Vendor = vendor;
        return ToModel(account);
    }

    public async Task<VendorBankAccountModel> UpdateNumbersAsync(
        int accountId, SaveVendorBankAccountRequestModel request, int userId, CancellationToken ct = default)
    {
        var account = await FindAsync(accountId, ct);
        var (routing, accountNumber, accountType) = ValidateNumbers(request);

        account.Nickname = request.Nickname.Trim();
        account.AccountType = accountType;
        account.RoutingNumberEncrypted = protector.Protect(routing)!;
        account.AccountNumberEncrypted = protector.Protect(accountNumber)!;
        account.RoutingNumberMasked = Mask(routing);
        account.AccountNumberMasked = Mask(accountNumber);

        // The destination is materially new: back to PendingApproval, and every downstream
        // attestation (approval, prenote, verification) is void.
        account.Status = VendorBankAccountStatus.PendingApproval;
        account.ChangedByUserId = userId;
        account.ApprovedByUserId = null;
        account.ApprovedAt = null;
        account.PrenoteSentAt = null;
        account.VerifiedAt = null;
        account.VerifiedByUserId = null;

        db.LogActivityAt(
            "bank-account-changed",
            $"Bank account '{account.Nickname}' numbers changed ({account.AccountNumberMasked}) — approval and prenote reset",
            ("Vendor", account.VendorId));
        await db.SaveChangesAsync(ct);
        return ToModel(account);
    }

    public async Task<VendorBankAccountModel> ApproveAsync(int accountId, int userId, CancellationToken ct = default)
    {
        var account = await FindAsync(accountId, ct);

        if (account.Status != VendorBankAccountStatus.PendingApproval)
            throw new InvalidOperationException("Only a pending bank account can be approved.");

        // Dual control: the user who made the change can never be the one who approves it.
        if (account.ChangedByUserId == userId)
            throw new InvalidOperationException(
                "Dual control: a bank-account change must be approved by a different user than the one who made it.");

        account.Status = VendorBankAccountStatus.Approved;
        account.ApprovedByUserId = userId;
        account.ApprovedAt = clock.UtcNow;

        db.LogActivityAt(
            "bank-account-approved",
            $"Bank account '{account.Nickname}' ({account.AccountNumberMasked}) approved (dual control)",
            ("Vendor", account.VendorId));
        await db.SaveChangesAsync(ct);
        return ToModel(account);
    }

    public async Task<VendorBankAccountModel> MarkVerifiedAsync(int accountId, int userId, CancellationToken ct = default)
    {
        var account = await FindAsync(accountId, ct);

        if (account.Status != VendorBankAccountStatus.PrenoteSent)
            throw new InvalidOperationException(
                "Only an account whose prenote has been sent can be marked verified (wait for the return window to pass).");

        account.Status = VendorBankAccountStatus.Verified;
        account.VerifiedAt = clock.UtcNow;
        account.VerifiedByUserId = userId;

        db.LogActivityAt(
            "bank-account-verified",
            $"Bank account '{account.Nickname}' ({account.AccountNumberMasked}) marked verified — prenote window passed",
            ("Vendor", account.VendorId));
        await db.SaveChangesAsync(ct);
        return ToModel(account);
    }

    public async Task<VendorBankAccountModel> DisableAsync(int accountId, int userId, CancellationToken ct = default)
    {
        var account = await FindAsync(accountId, ct);

        if (account.Status == VendorBankAccountStatus.Disabled)
            throw new InvalidOperationException("Bank account is already disabled.");

        account.Status = VendorBankAccountStatus.Disabled;

        db.LogActivityAt(
            "bank-account-disabled",
            $"Bank account '{account.Nickname}' ({account.AccountNumberMasked}) disabled",
            ("Vendor", account.VendorId));
        await db.SaveChangesAsync(ct);
        return ToModel(account);
    }

    public async Task<VendorBankAccount?> ResolvePayableAccountAsync(int vendorId, CancellationToken ct = default)
    {
        var requirePrenote = await settings.GetBoolAsync(BankingSettings.RequirePrenoteKey, ct);

        var query = db.VendorBankAccounts.Where(a => a.VendorId == vendorId);
        query = requirePrenote
            ? query.Where(a => a.Status == VendorBankAccountStatus.Verified)
            : query.Where(a => a.Status == VendorBankAccountStatus.Verified
                || a.Status == VendorBankAccountStatus.Approved
                || a.Status == VendorBankAccountStatus.PrenoteSent);

        return await query.OrderByDescending(a => a.Id).FirstOrDefaultAsync(ct);
    }

    private async Task<VendorBankAccount> FindAsync(int accountId, CancellationToken ct)
        => await db.VendorBankAccounts.Include(a => a.Vendor).FirstOrDefaultAsync(a => a.Id == accountId, ct)
            ?? throw new KeyNotFoundException($"Vendor bank account {accountId} not found");

    private static (string Routing, string Account, BankAccountType Type) ValidateNumbers(
        SaveVendorBankAccountRequestModel request)
    {
        var routing = new string((request.RoutingNumber ?? string.Empty).Where(char.IsAsciiDigit).ToArray());
        var accountNumber = new string((request.AccountNumber ?? string.Empty).Where(char.IsAsciiDigit).ToArray());

        if (!NachaFileGenerator.IsValidRoutingNumber(routing))
            throw new InvalidOperationException("Routing number failed the ABA checksum — check for a typo.");
        if (accountNumber.Length is < 4 or > 17)
            throw new InvalidOperationException("Account number must be 4–17 digits.");
        if (string.IsNullOrWhiteSpace(request.Nickname))
            throw new InvalidOperationException("A nickname is required.");
        if (!Enum.TryParse<BankAccountType>(request.AccountType, ignoreCase: true, out var accountType))
            throw new InvalidOperationException($"Unknown account type '{request.AccountType}' (Checking or Savings).");

        return (routing, accountNumber, accountType);
    }

    private static string Mask(string number)
        => new string('•', Math.Max(0, number.Length - 4)) + number[^Math.Min(4, number.Length)..];

    private static VendorBankAccountModel ToModel(VendorBankAccount a)
        => new(
            a.Id, a.VendorId, a.Vendor?.CompanyName ?? $"Vendor {a.VendorId}", a.Nickname,
            a.AccountType.ToString(), a.RoutingNumberMasked, a.AccountNumberMasked, a.Status.ToString(),
            a.ChangedByUserId, a.ApprovedByUserId, a.ApprovedAt, a.PrenoteSentAt, a.VerifiedAt, a.CreatedAt);
}
