using System.Security.Cryptography;

using FluentValidation;
using MediatR;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Forge.Data.Context;

namespace Forge.Api.Features.Auth;

public record SetPinCommand(string Pin) : IRequest;

public class SetPinValidator : AbstractValidator<SetPinCommand>
{
    public SetPinValidator()
    {
        RuleFor(x => x.Pin).NotEmpty().Length(4, 8)
            .Matches(@"^\d+$").WithMessage("PIN must contain only digits");
    }
}

public class SetPinHandler(
    IHttpContextAccessor httpContext,
    UserManager<ApplicationUser> userManager) : IRequestHandler<SetPinCommand>
{
    public async Task Handle(SetPinCommand request, CancellationToken cancellationToken)
    {
        var userId = httpContext.HttpContext!.User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)!.Value;
        var user = await userManager.FindByIdAsync(userId)
            ?? throw new KeyNotFoundException("User not found");

        user.PinHash = HashPin(request.Pin);
        user.UpdatedAt = DateTimeOffset.UtcNow;
        await userManager.UpdateAsync(user);
    }

    internal static string HashPin(string pin)
    {
        var salt = RandomNumberGenerator.GetBytes(16);
        var hash = Rfc2898DeriveBytes.Pbkdf2(pin, salt, 100_000, HashAlgorithmName.SHA256, 32);
        var combined = new byte[salt.Length + hash.Length];
        Buffer.BlockCopy(salt, 0, combined, 0, salt.Length);
        Buffer.BlockCopy(hash, 0, combined, salt.Length, hash.Length);
        return Convert.ToBase64String(combined);
    }

    internal static bool VerifyPin(string pin, string storedHash)
    {
        var combined = Convert.FromBase64String(storedHash);
        var salt = combined[..16];
        var storedHashBytes = combined[16..];
        var hash = Rfc2898DeriveBytes.Pbkdf2(pin, salt, 100_000, HashAlgorithmName.SHA256, 32);
        return CryptographicOperations.FixedTimeEquals(hash, storedHashBytes);
    }
}
