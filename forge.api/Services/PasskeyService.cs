using System.Text;

using Fido2NetLib;
using Fido2NetLib.Objects;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;

using Forge.Core.Entities;
using Forge.Core.Enums;
using Forge.Core.Interfaces;
using Forge.Data.Context;

namespace Forge.Api.Services;

public class PasskeyService(
    AppDbContext db,
    IMemoryCache cache,
    IClock clock,
    ILogger<PasskeyService> logger) : IPasskeyService
{
    private static readonly TimeSpan ChallengeTtl = TimeSpan.FromMinutes(5);

    private static string RegKey(int userId) => $"passkey:reg:{userId}";
    private static string AssertKey(int userId) => $"passkey:assert:{userId}";

    private static IFido2 CreateFido2(string origin)
    {
        var host = new Uri(origin).Host;
        return new Fido2(new Fido2Configuration
        {
            ServerDomain = host,
            ServerName = "Forge",
            Origins = new HashSet<string> { origin },
        });
    }

    public async Task<CredentialCreateOptions> BeginRegistrationAsync(
        int userId, string origin, CancellationToken ct)
    {
        var user = await db.Users.AsNoTracking()
            .FirstAsync(u => u.Id == userId, ct);

        var fidoUser = new Fido2User
        {
            Id = Encoding.UTF8.GetBytes(userId.ToString()),
            Name = user.Email ?? userId.ToString(),
            DisplayName = $"{user.FirstName} {user.LastName}".Trim(),
        };

        var existing = await ExistingCredentialsAsync(userId, ct);

        var options = CreateFido2(origin).RequestNewCredential(new RequestNewCredentialParams
        {
            User = fidoUser,
            ExcludeCredentials = existing,
            AuthenticatorSelection = new AuthenticatorSelection
            {
                ResidentKey = ResidentKeyRequirement.Preferred,
                UserVerification = UserVerificationRequirement.Required,
            },
            AttestationPreference = AttestationConveyancePreference.None,
        });

        cache.Set(RegKey(userId), options.ToJson(), ChallengeTtl);
        return options;
    }

    public async Task<string> CompleteRegistrationAsync(
        int userId, string origin, AuthenticatorAttestationRawResponse response,
        string? deviceName, CancellationToken ct)
    {
        if (!cache.TryGetValue<string>(RegKey(userId), out var optionsJson) || optionsJson is null)
            throw new InvalidOperationException("No pending passkey registration. Start over.");
        cache.Remove(RegKey(userId));

        var options = CredentialCreateOptions.FromJson(optionsJson);

        var credential = await CreateFido2(origin).MakeNewCredentialAsync(new MakeNewCredentialParams
        {
            AttestationResponse = response,
            OriginalOptions = options,
            IsCredentialIdUniqueToUserCallback = async (args, token) =>
                !await db.UserMfaDevices.AnyAsync(
                    d => d.CredentialId == Convert.ToBase64String(args.CredentialId), token),
        }, ct);

        var name = string.IsNullOrWhiteSpace(deviceName) ? "Passkey" : deviceName.Trim();
        db.UserMfaDevices.Add(new UserMfaDevice
        {
            UserId = userId,
            DeviceType = MfaDeviceType.WebAuthn,
            DeviceName = name,
            IsVerified = true,
            EncryptedSecret = string.Empty,
            CredentialId = Convert.ToBase64String(credential.Id),
            PublicKey = Convert.ToBase64String(credential.PublicKey),
            SignCount = credential.SignCount,
        });
        await db.SaveChangesAsync(ct);

        logger.LogInformation("Passkey registered for user {UserId}", userId);
        return name;
    }

    public async Task<AssertionOptions?> BeginAssertionAsync(
        int userId, string origin, CancellationToken ct)
    {
        var credentials = await ExistingCredentialsAsync(userId, ct);
        if (credentials.Count == 0) return null;

        var options = CreateFido2(origin).GetAssertionOptions(new GetAssertionOptionsParams
        {
            AllowedCredentials = credentials,
            UserVerification = UserVerificationRequirement.Required,
        });

        cache.Set(AssertKey(userId), options.ToJson(), ChallengeTtl);
        return options;
    }

    public async Task<bool> CompleteAssertionAsync(
        int userId, string origin, AuthenticatorAssertionRawResponse response, CancellationToken ct)
    {
        if (!cache.TryGetValue<string>(AssertKey(userId), out var optionsJson) || optionsJson is null)
            return false;
        cache.Remove(AssertKey(userId));

        var credentialId = Convert.ToBase64String(response.RawId);
        var device = await db.UserMfaDevices.FirstOrDefaultAsync(
            d => d.UserId == userId && d.CredentialId == credentialId && d.IsVerified, ct);
        if (device?.PublicKey is null) return false;

        var options = AssertionOptions.FromJson(optionsJson);

        try
        {
            var result = await CreateFido2(origin).MakeAssertionAsync(new MakeAssertionParams
            {
                AssertionResponse = response,
                OriginalOptions = options,
                StoredPublicKey = Convert.FromBase64String(device.PublicKey),
                StoredSignatureCounter = device.SignCount ?? 0,
                IsUserHandleOwnerOfCredentialIdCallback = (args, _) =>
                    Task.FromResult(Encoding.UTF8.GetString(args.UserHandle) == userId.ToString()),
            }, ct);

            device.SignCount = result.SignCount;
            device.LastUsedAt = clock.UtcNow;
            await db.SaveChangesAsync(ct);
            return true;
        }
        catch (Fido2VerificationException ex)
        {
            logger.LogWarning(ex, "Passkey assertion failed for user {UserId}", userId);
            return false;
        }
    }

    private async Task<List<PublicKeyCredentialDescriptor>> ExistingCredentialsAsync(
        int userId, CancellationToken ct)
    {
        var ids = await db.UserMfaDevices.AsNoTracking()
            .Where(d => d.UserId == userId
                && d.DeviceType == MfaDeviceType.WebAuthn
                && d.IsVerified
                && d.CredentialId != null)
            .Select(d => d.CredentialId!)
            .ToListAsync(ct);

        return ids.Select(id => new PublicKeyCredentialDescriptor(Convert.FromBase64String(id)))
            .ToList();
    }
}
