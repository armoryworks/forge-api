using System.Security.Cryptography;
using System.Text.Json;

using FluentValidation;
using MediatR;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Forge.Core.Entities;
using Forge.Core.Enums;
using Forge.Core.Interfaces;
using Forge.Core.Models;
using Forge.Data.Context;

namespace Forge.Api.Features.Admin;

public record CreateAdminUserCommand(
    string Email,
    string FirstName,
    string LastName,
    string? Initials,
    string? AvatarColor,
    List<string> Roles) : IRequest<CreateAdminUserResponseModel>;

public record CreateAdminUserResponseModel(
    int Id,
    string Email,
    string FirstName,
    string LastName,
    string? Initials,
    string? AvatarColor,
    bool IsActive,
    string[] Roles,
    DateTimeOffset CreatedAt,
    string SetupToken,
    DateTimeOffset SetupTokenExpiresAt);

public class CreateAdminUserValidator : AbstractValidator<CreateAdminUserCommand>
{
    public CreateAdminUserValidator()
    {
        RuleFor(x => x.Email).NotEmpty().EmailAddress().MaximumLength(256);
        RuleFor(x => x.FirstName).NotEmpty().MaximumLength(100);
        RuleFor(x => x.LastName).NotEmpty().MaximumLength(100);
        RuleFor(x => x.Initials).MaximumLength(4).When(x => x.Initials is not null);
        RuleFor(x => x.AvatarColor).MaximumLength(20).When(x => x.AvatarColor is not null);
        RuleFor(x => x.Roles).NotEmpty();
        RuleForEach(x => x.Roles).NotEmpty().MaximumLength(50);
    }
}

public class CreateAdminUserHandler(
    UserManager<ApplicationUser> userManager,
    IBarcodeService barcodeService,
    AppDbContext db)
    : IRequestHandler<CreateAdminUserCommand, CreateAdminUserResponseModel>
{
    public async Task<CreateAdminUserResponseModel> Handle(CreateAdminUserCommand request, CancellationToken cancellationToken)
    {
        var user = new ApplicationUser
        {
            UserName = request.Email,
            Email = request.Email,
            FirstName = request.FirstName,
            LastName = request.LastName,
            Initials = request.Initials ?? GenerateInitials(request.FirstName, request.LastName),
            AvatarColor = request.AvatarColor ?? "#94a3b8",
            EmailConfirmed = true,
        };

        var result = await userManager.CreateAsync(user);
        if (!result.Succeeded)
        {
            var errors = string.Join("; ", result.Errors.Select(e => e.Description));
            throw new InvalidOperationException($"Failed to create user: {errors}");
        }

        var assignedRoles = request.Roles
            .Where(r => !string.IsNullOrWhiteSpace(r))
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        await userManager.AddToRolesAsync(user, assignedRoles);

        await barcodeService.CreateBarcodeAsync(
            BarcodeEntityType.User, user.Id, $"{user.Id:D6}", cancellationToken);

        // Generate short setup code (XXXX-XXXX) — easy to read aloud or write down.
        // The plaintext token is returned to the caller (admin) once and never stored;
        // only its SHA-256 hash is persisted so a DB breach cannot impersonate enrollees.
        const string chars = "ABCDEFGHJKLMNPQRSTUVWXYZ23456789"; // no 0/O/1/I to avoid confusion
        var bytes = RandomNumberGenerator.GetBytes(8);
        var code = new char[8];
        for (var i = 0; i < 8; i++)
            code[i] = chars[bytes[i] % chars.Length];
        var token = $"{new string(code, 0, 4)}-{new string(code, 4, 4)}";

        user.SetupToken = HashSetupToken(token); // F-035: store hash, not plaintext
        user.SetupTokenExpiresAt = DateTimeOffset.UtcNow.AddDays(7);
        user.UpdatedAt = DateTimeOffset.UtcNow;

        await userManager.UpdateAsync(user);

        // Auto-enroll new user in matching training paths
        var autoPaths = await db.TrainingPaths
            .Where(p => p.IsAutoAssigned && p.IsActive && p.DeletedAt == null)
            .ToListAsync(cancellationToken);

        foreach (var path in autoPaths)
        {
            var allowedRoles = string.IsNullOrEmpty(path.AllowedRoles)
                ? null
                : JsonSerializer.Deserialize<string[]>(path.AllowedRoles);

            if (allowedRoles == null || allowedRoles.Intersect(assignedRoles, StringComparer.Ordinal).Any())
            {
                db.TrainingPathEnrollments.Add(new TrainingPathEnrollment
                {
                    UserId = user.Id,
                    PathId = path.Id,
                    IsAutoAssigned = true,
                });
            }
        }

        if (autoPaths.Count > 0)
            await db.SaveChangesAsync(cancellationToken);

        return new CreateAdminUserResponseModel(
            user.Id,
            user.Email,
            user.FirstName,
            user.LastName,
            user.Initials,
            user.AvatarColor,
            user.IsActive,
            assignedRoles,
            user.CreatedAt,
            token,
            user.SetupTokenExpiresAt.Value);
    }

    private static string GenerateInitials(string firstName, string lastName)
    {
        var first = string.IsNullOrEmpty(firstName) ? "" : firstName[..1].ToUpper();
        var last = string.IsNullOrEmpty(lastName) ? "" : lastName[..1].ToUpper();
        return first + last;
    }

    internal static string HashSetupToken(string token)
    {
        var normalized = token.Trim().ToUpperInvariant();
        var hashBytes = System.Security.Cryptography.SHA256.HashData(
            System.Text.Encoding.UTF8.GetBytes(normalized));
        return Convert.ToHexString(hashBytes);
    }
}
