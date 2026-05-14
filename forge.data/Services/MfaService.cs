using System.Security.Cryptography;
using System.Text;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;

using OtpNet;

using Forge.Core.Entities;
using Forge.Core.Interfaces;
using Forge.Core.Models;
using Forge.Data.Context;

namespace Forge.Data.Services;

public class MfaService(
    AppDbContext db,
    ITokenEncryptionService encryption,
    ITokenService tokenService,
    ISessionStore sessionStore,
    IMemoryCache cache,
    ILogger<MfaService> logger) : IMfaService
{
    private const int MaxFailedAttempts = 5;
    private static readonly TimeSpan LockoutDuration = TimeSpan.FromMinutes(5);
    private static readonly TimeSpan ChallengeExpiry = TimeSpan.FromMinutes(5);
    private const int RecoveryCodeCount = 10;
    private const string Issuer = "Forge";

    // ── Setup ──────────────────────────────────────────────

    public async Task<MfaSetupResponseModel> BeginTotpSetupAsync(int userId, string? deviceName, CancellationToken ct)
    {
        var secret = GenerateTotpSecret();

        var device = new UserMfaDevice
        {
            UserId = userId,
            DeviceType = MfaDeviceType.Totp,
            EncryptedSecret = encryption.Encrypt(secret),
            DeviceName = deviceName ?? "Authenticator App",
            IsVerified = false,
            IsDefault = false,
        };

        db.UserMfaDevices.Add(device);
        await db.SaveChangesAsync(ct);

        var user = await db.Users.FindAsync([userId], ct);
        var email = user?.Email ?? "user@forge.local";

        return new MfaSetupResponseModel
        {
            Secret = secret,
            QrCodeUri = GenerateQrCodeUri(secret, email, Issuer),
            ManualEntryKey = Base32Encoding.ToString(Encoding.UTF8.GetBytes(secret)),
            DeviceId = device.Id,
        };
    }

    public async Task<bool> VerifyTotpSetupAsync(int userId, int deviceId, string code, CancellationToken ct)
    {
        var device = await db.UserMfaDevices
            .FirstOrDefaultAsync(d => d.Id == deviceId && d.UserId == userId, ct);

        if (device is null) return false;

        var secret = encryption.Decrypt(device.EncryptedSecret);
        if (!ValidateTotpCode(secret, code)) return false;

        device.IsVerified = true;

        // Set as default if no other default exists
        var hasDefault = await db.UserMfaDevices
            .AnyAsync(d => d.UserId == userId && d.IsDefault && d.Id != deviceId, ct);

        if (!hasDefault)
            device.IsDefault = true;

        // Enable MFA on user
        var user = await db.Users.FindAsync([userId], ct);
        if (user is not null && !user.MfaEnabled)
        {
            user.MfaEnabled = true;
            user.MfaEnabledAt = DateTimeOffset.UtcNow;
        }

        await db.SaveChangesAsync(ct);

        logger.LogInformation("MFA TOTP device {DeviceId} verified for user {UserId}", deviceId, userId);
        return true;
    }

    public async Task DisableMfaAsync(int userId, CancellationToken ct)
    {
        var user = await db.Users.FindAsync([userId], ct)
            ?? throw new KeyNotFoundException("User not found");

        // Remove all devices
        var devices = await db.UserMfaDevices
            .Where(d => d.UserId == userId)
            .ToListAsync(ct);

        foreach (var device in devices)
        {
            device.DeletedAt = DateTimeOffset.UtcNow;
        }

        // Remove recovery codes
        var codes = await db.MfaRecoveryCodes
            .Where(c => c.UserId == userId)
            .ToListAsync(ct);

        foreach (var code in codes)
        {
            code.DeletedAt = DateTimeOffset.UtcNow;
        }

        user.MfaEnabled = false;
        user.MfaEnabledAt = null;
        user.MfaRecoveryCodesRemaining = 0;

        await db.SaveChangesAsync(ct);
        logger.LogInformation("MFA disabled for user {UserId}", userId);
    }

    public async Task RemoveDeviceAsync(int userId, int deviceId, CancellationToken ct)
    {
        var device = await db.UserMfaDevices
            .FirstOrDefaultAsync(d => d.Id == deviceId && d.UserId == userId, ct)
            ?? throw new KeyNotFoundException("Device not found");

        // Check if this is the last verified device and MFA is enforced
        var user = await db.Users.FindAsync([userId], ct);
        if (user?.MfaEnforcedByPolicy == true)
        {
            var otherVerified = await db.UserMfaDevices
                .AnyAsync(d => d.UserId == userId && d.Id != deviceId && d.IsVerified, ct);

            if (!otherVerified)
                throw new InvalidOperationException("Cannot remove last MFA device when MFA is required by policy");
        }

        device.DeletedAt = DateTimeOffset.UtcNow;

        // If this was the default, promote another
        if (device.IsDefault)
        {
            var next = await db.UserMfaDevices
                .FirstOrDefaultAsync(d => d.UserId == userId && d.Id != deviceId && d.IsVerified, ct);

            if (next is not null)
                next.IsDefault = true;
        }

        // If no verified devices remain, disable MFA
        var hasVerified = await db.UserMfaDevices
            .AnyAsync(d => d.UserId == userId && d.Id != deviceId && d.IsVerified, ct);

        if (!hasVerified && user is not null)
        {
            user.MfaEnabled = false;
            user.MfaEnabledAt = null;
        }

        await db.SaveChangesAsync(ct);
    }

    // ── Challenge/Validate ─────────────────────────────────

    public async Task<MfaChallengeResponseModel> CreateChallengeAsync(int userId, CancellationToken ct)
    {
        var device = await db.UserMfaDevices
            .Where(d => d.UserId == userId && d.IsVerified)
            .OrderByDescending(d => d.IsDefault)
            .FirstOrDefaultAsync(ct)
            ?? throw new InvalidOperationException("No verified MFA device found");

        if (device.LockedUntil > DateTimeOffset.UtcNow)
            throw new InvalidOperationException("MFA device is temporarily locked due to too many failed attempts");

        var challengeToken = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));
        var cacheKey = $"mfa-challenge:{challengeToken}";

        cache.Set(cacheKey, new MfaChallengeData(userId, device.Id), ChallengeExpiry);

        string? maskedTarget = device.DeviceType switch
        {
            MfaDeviceType.Sms when device.PhoneNumber is not null =>
                $"***-***-{device.PhoneNumber[^4..]}",
            MfaDeviceType.Email when device.EmailAddress is not null =>
                MaskEmail(device.EmailAddress),
            _ => null,
        };

        return new MfaChallengeResponseModel
        {
            ChallengeToken = challengeToken,
            DeviceType = device.DeviceType,
            MaskedTarget = maskedTarget,
        };
    }

    public async Task<MfaValidateResponseModel?> ValidateChallengeAsync(
        string challengeToken, string code, bool rememberDevice, CancellationToken ct)
    {
        var cacheKey = $"mfa-challenge:{challengeToken}";
        if (!cache.TryGetValue(cacheKey, out MfaChallengeData? challengeData) || challengeData is null)
            return null;

        var device = await db.UserMfaDevices.FindAsync([challengeData.DeviceId], ct);
        if (device is null) return null;

        if (device.LockedUntil > DateTimeOffset.UtcNow)
            return null;

        var secret = encryption.Decrypt(device.EncryptedSecret);
        if (!ValidateTotpCode(secret, code))
        {
            device.FailedAttempts++;
            if (device.FailedAttempts >= MaxFailedAttempts)
            {
                device.LockedUntil = DateTimeOffset.UtcNow.Add(LockoutDuration);
                logger.LogWarning("MFA device {DeviceId} locked for user {UserId} after {Attempts} failed attempts",
                    device.Id, device.UserId, device.FailedAttempts);
            }
            await db.SaveChangesAsync(ct);
            return null;
        }

        // Success — reset attempts, update last used
        device.FailedAttempts = 0;
        device.LockedUntil = null;
        device.LastUsedAt = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync(ct);

        // Invalidate challenge
        cache.Remove(cacheKey);

        return await GenerateFullTokenAsync(challengeData.UserId, ct);
    }

    // ── Recovery ───────────────────────────────────────────

    public async Task<MfaRecoveryCodesResponseModel> GenerateRecoveryCodesAsync(int userId, CancellationToken ct)
    {
        // Soft-delete existing codes
        var existing = await db.MfaRecoveryCodes
            .Where(c => c.UserId == userId)
            .ToListAsync(ct);

        foreach (var old in existing)
            old.DeletedAt = DateTimeOffset.UtcNow;

        var codes = new List<string>();
        for (var i = 0; i < RecoveryCodeCount; i++)
        {
            var code = GenerateRecoveryCode();
            codes.Add(code);

            db.MfaRecoveryCodes.Add(new MfaRecoveryCode
            {
                UserId = userId,
                CodeHash = HashRecoveryCode(code),
            });
        }

        var user = await db.Users.FindAsync([userId], ct);
        if (user is not null)
            user.MfaRecoveryCodesRemaining = RecoveryCodeCount;

        await db.SaveChangesAsync(ct);

        return new MfaRecoveryCodesResponseModel { Codes = codes };
    }

    public async Task<MfaValidateResponseModel?> ValidateRecoveryCodeAsync(
        string challengeToken, string recoveryCode, CancellationToken ct)
    {
        var cacheKey = $"mfa-challenge:{challengeToken}";
        if (!cache.TryGetValue(cacheKey, out MfaChallengeData? challengeData) || challengeData is null)
            return null;

        var codeHash = HashRecoveryCode(recoveryCode.Trim().ToUpperInvariant());

        var code = await db.MfaRecoveryCodes
            .FirstOrDefaultAsync(c => c.UserId == challengeData.UserId && c.CodeHash == codeHash && !c.IsUsed, ct);

        if (code is null) return null;

        code.IsUsed = true;
        code.UsedAt = DateTimeOffset.UtcNow;

        var user = await db.Users.FindAsync([challengeData.UserId], ct);
        if (user is not null)
            user.MfaRecoveryCodesRemaining = Math.Max(0, user.MfaRecoveryCodesRemaining - 1);

        await db.SaveChangesAsync(ct);

        cache.Remove(cacheKey);

        logger.LogInformation("Recovery code used for user {UserId}. {Remaining} codes remaining",
            challengeData.UserId, user?.MfaRecoveryCodesRemaining ?? 0);

        return await GenerateFullTokenAsync(challengeData.UserId, ct);
    }

    // ── TOTP ───────────────────────────────────────────────

    public bool ValidateTotpCode(string secret, string code, int toleranceSteps = 1)
    {
        try
        {
            var secretBytes = Encoding.UTF8.GetBytes(secret);
            var totp = new Totp(secretBytes, step: 30, totpSize: 6);
            return totp.VerifyTotp(code, out _, new VerificationWindow(toleranceSteps, toleranceSteps));
        }
        catch
        {
            return false;
        }
    }

    public string GenerateTotpSecret()
    {
        var key = KeyGeneration.GenerateRandomKey(20);
        return Base32Encoding.ToString(key);
    }

    public string GenerateQrCodeUri(string secret, string email, string issuer)
    {
        return $"otpauth://totp/{Uri.EscapeDataString(issuer)}:{Uri.EscapeDataString(email)}" +
               $"?secret={secret}&issuer={Uri.EscapeDataString(issuer)}&digits=6&period=30";
    }

    // ── Status ─────────────────────────────────────────────

    public async Task<MfaStatusResponseModel> GetMfaStatusAsync(int userId, CancellationToken ct)
    {
        var user = await db.Users.FindAsync([userId], ct);

        var devices = await db.UserMfaDevices
            .Where(d => d.UserId == userId)
            .AsNoTracking()
            .Select(d => new MfaDeviceSummary
            {
                Id = d.Id,
                DeviceType = d.DeviceType,
                DeviceName = d.DeviceName,
                IsDefault = d.IsDefault,
                IsVerified = d.IsVerified,
                LastUsedAt = d.LastUsedAt,
            })
            .ToListAsync(ct);

        return new MfaStatusResponseModel
        {
            IsEnabled = user?.MfaEnabled ?? false,
            IsEnforcedByPolicy = user?.MfaEnforcedByPolicy ?? false,
            Devices = devices,
            RecoveryCodesRemaining = user?.MfaRecoveryCodesRemaining ?? 0,
        };
    }

    public async Task<bool> IsMfaRequiredAsync(int userId, CancellationToken ct)
    {
        var user = await db.Users.FindAsync([userId], ct);
        return user?.MfaEnabled == true || user?.MfaEnforcedByPolicy == true;
    }

    // ── Helpers ─────────────────────────────────────────────

    private async Task<MfaValidateResponseModel> GenerateFullTokenAsync(int userId, CancellationToken ct)
    {
        var user = await db.Users.FindAsync([userId], ct)
            ?? throw new KeyNotFoundException("User not found");

        var roles = await db.UserRoles
            .Where(ur => ur.UserId == userId)
            .Join(db.Roles, ur => ur.RoleId, r => r.Id, (_, r) => r.Name!)
            .ToListAsync(ct);

        var result = tokenService.GenerateToken(
            user.Id, user.Email!, user.FirstName, user.LastName,
            user.Initials, user.AvatarColor, roles);

        await sessionStore.CreateSessionAsync(user.Id, result.Jti, result.ExpiresAt,
            "mfa", null, null, ct);

        return new MfaValidateResponseModel
        {
            AccessToken = result.Token,
            RefreshToken = result.Jti, // Simplified — in production use a separate refresh token
            ExpiresAt = result.ExpiresAt,
        };
    }

    private static string GenerateRecoveryCode()
    {
        var bytes = RandomNumberGenerator.GetBytes(5);
        var code = Convert.ToHexString(bytes).ToUpperInvariant();
        return $"{code[..5]}-{code[5..]}";
    }

    private static string HashRecoveryCode(string code)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(code.Trim().ToUpperInvariant()));
        return Convert.ToBase64String(bytes);
    }

    private static string MaskEmail(string email)
    {
        var parts = email.Split('@');
        if (parts.Length != 2) return "***@***.***";
        var local = parts[0];
        var domain = parts[1];
        var maskedLocal = local.Length > 2
            ? $"{local[0]}***{local[^1]}"
            : "***";
        var domainParts = domain.Split('.');
        var maskedDomain = domainParts.Length >= 2
            ? $"{domainParts[0][0]}***.{domainParts[^1]}"
            : "***";
        return $"{maskedLocal}@{maskedDomain}";
    }

    private record MfaChallengeData(int UserId, int DeviceId);
}
