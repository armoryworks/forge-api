using System.Security.Cryptography;

using MediatR;
using Microsoft.AspNetCore.Identity;
using Forge.Data.Context;

namespace Forge.Api.Features.Admin;

public record GenerateSetupTokenCommand(int UserId) : IRequest<SetupTokenResponseModel>;

public record SetupTokenResponseModel(string Token, DateTimeOffset ExpiresAt);

public class GenerateSetupTokenHandler(
    UserManager<ApplicationUser> userManager) : IRequestHandler<GenerateSetupTokenCommand, SetupTokenResponseModel>
{
    public async Task<SetupTokenResponseModel> Handle(GenerateSetupTokenCommand request, CancellationToken cancellationToken)
    {
        var user = await userManager.FindByIdAsync(request.UserId.ToString())
            ?? throw new KeyNotFoundException($"User {request.UserId} not found");

        // Short alphanumeric code (8 chars, grouped as XXXX-XXXX) — easy to read aloud or write down
        const string chars = "ABCDEFGHJKLMNPQRSTUVWXYZ23456789"; // no 0/O/1/I to avoid confusion
        var bytes = RandomNumberGenerator.GetBytes(8);
        var code = new char[8];
        for (var i = 0; i < 8; i++)
            code[i] = chars[bytes[i] % chars.Length];
        var token = $"{new string(code, 0, 4)}-{new string(code, 4, 4)}";

        user.SetupToken = token;
        user.SetupTokenExpiresAt = DateTimeOffset.UtcNow.AddDays(7);
        user.UpdatedAt = DateTimeOffset.UtcNow;

        await userManager.UpdateAsync(user);

        return new SetupTokenResponseModel(token, user.SetupTokenExpiresAt.Value);
    }
}
