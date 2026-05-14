using System.Security.Cryptography;
using System.Text.Json;

using FluentValidation;

using MediatR;

using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

using Forge.Api.Services;
using Forge.Core.Entities;
using Forge.Core.Interfaces;
using Forge.Core.Models;
using Forge.Data.Context;

namespace Forge.Api.Features.Employees;

/// <summary>
/// Phase 3 / WU-19 / F9 — promote an existing User-less Employee to also
/// have a User account (system access). Creates the User, links via
/// <c>EmployeeProfile.UserId</c>, issues a setup token. Mirrors the
/// <c>CreateAdminUser</c> path for the User side.
/// </summary>
public record GrantSystemAccessCommand(
    int EmployeeId,
    string Email,
    string Role) : IRequest<GrantSystemAccessResponseModel>;

public record GrantSystemAccessRequest(string Email, string Role);

public record GrantSystemAccessResponseModel(
    int EmployeeId,
    int UserId,
    string Email,
    string Role,
    string SetupToken,
    DateTimeOffset SetupTokenExpiresAt);

public class GrantSystemAccessValidator : AbstractValidator<GrantSystemAccessCommand>
{
    public GrantSystemAccessValidator()
    {
        RuleFor(x => x.EmployeeId).GreaterThan(0);
        RuleFor(x => x.Email).NotEmpty().EmailAddress().MaximumLength(256);
        RuleFor(x => x.Role).NotEmpty().MaximumLength(50);
    }
}

public class GrantSystemAccessHandler(
    AppDbContext db,
    UserManager<ApplicationUser> userManager,
    IBarcodeService barcodeService,
    ISystemAuditWriter audit,
    IHttpContextAccessor httpContext)
    : IRequestHandler<GrantSystemAccessCommand, GrantSystemAccessResponseModel>
{
    public async Task<GrantSystemAccessResponseModel> Handle(GrantSystemAccessCommand request, CancellationToken ct)
    {
        var profile = await db.EmployeeProfiles
            .FirstOrDefaultAsync(p => p.Id == request.EmployeeId && p.DeletedAt == null, ct)
            ?? throw new InvalidOperationException($"Employee {request.EmployeeId} not found");

        if (profile.UserId is not null)
            throw new InvalidOperationException(
                $"Employee {request.EmployeeId} already has system access (userId={profile.UserId})");

        var firstName = profile.FirstName ?? string.Empty;
        var lastName = profile.LastName ?? string.Empty;

        var user = new ApplicationUser
        {
            UserName = request.Email,
            Email = request.Email,
            FirstName = firstName,
            LastName = lastName,
            Initials = GenerateInitials(firstName, lastName),
            AvatarColor = "#94a3b8",
            EmailConfirmed = true,
        };

        var result = await userManager.CreateAsync(user);
        if (!result.Succeeded)
        {
            var errors = string.Join("; ", result.Errors.Select(e => e.Description));
            throw new InvalidOperationException($"Failed to create user: {errors}");
        }

        await userManager.AddToRoleAsync(user, request.Role);

        await barcodeService.CreateBarcodeAsync(
            BarcodeEntityType.User, user.Id, $"{user.Id:D6}", ct);

        // Setup-token generation — mirrors CreateAdminUser
        const string chars = "ABCDEFGHJKLMNPQRSTUVWXYZ23456789";
        var bytes = RandomNumberGenerator.GetBytes(8);
        var code = new char[8];
        for (var i = 0; i < 8; i++)
            code[i] = chars[bytes[i] % chars.Length];
        var token = $"{new string(code, 0, 4)}-{new string(code, 4, 4)}";

        user.SetupToken = token;
        user.SetupTokenExpiresAt = DateTimeOffset.UtcNow.AddDays(7);
        user.UpdatedAt = DateTimeOffset.UtcNow;
        await userManager.UpdateAsync(user);

        // Link the EmployeeProfile to the new User
        profile.UserId = user.Id;
        await db.SaveChangesAsync(ct);

        var actorId = TryGetActorId(httpContext);
        await audit.WriteAsync(
            "EmployeeUserLinked",
            actorId,
            entityType: "EmployeeProfile",
            entityId: profile.Id,
            details: JsonSerializer.Serialize(new
            {
                userId = user.Id,
                email = request.Email,
                role = request.Role,
            }),
            ct: ct);

        return new GrantSystemAccessResponseModel(
            EmployeeId: profile.Id,
            UserId: user.Id,
            Email: request.Email,
            Role: request.Role,
            SetupToken: token,
            SetupTokenExpiresAt: user.SetupTokenExpiresAt.Value);
    }

    private static int TryGetActorId(IHttpContextAccessor http)
    {
        var v = http.HttpContext?.User?.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;
        return int.TryParse(v, out var id) ? id : 0;
    }

    private static string GenerateInitials(string firstName, string lastName)
    {
        var first = string.IsNullOrEmpty(firstName) ? "" : firstName[..1].ToUpper();
        var last = string.IsNullOrEmpty(lastName) ? "" : lastName[..1].ToUpper();
        return first + last;
    }
}
