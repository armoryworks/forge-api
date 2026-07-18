using FluentValidation;
using MediatR;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

using Forge.Api.Features.Auth;
using Forge.Data.Context;
using Forge.Data.Extensions;

namespace Forge.Api.Features.Admin;

/// <summary>
/// One-shot kiosk identity provisioning: creates the user with the given
/// role, assigns the kiosk barcode, and hashes the PIN through the exact
/// <see cref="SetPinHandler"/> PBKDF2 code path. Closes the chicken/egg in
/// scripted bring-up (dev/CI/integration installs), which previously needed
/// an already-authenticated set-pin call or a direct SQL insert to mint a
/// barcode+PIN identity.
/// </summary>
public record CreateKioskUserCommand(
    string Email,
    string FirstName,
    string LastName,
    string Role,
    string Barcode,
    string Pin) : IRequest<CreateKioskUserResponseModel>;

public record CreateKioskUserResponseModel(
    int Id,
    string Email,
    string FirstName,
    string LastName,
    string Role,
    string Barcode,
    DateTimeOffset CreatedAt);

public class CreateKioskUserValidator : AbstractValidator<CreateKioskUserCommand>
{
    public CreateKioskUserValidator()
    {
        RuleFor(x => x.Email).NotEmpty().EmailAddress().MaximumLength(256);
        RuleFor(x => x.FirstName).NotEmpty().MaximumLength(100);
        RuleFor(x => x.LastName).NotEmpty().MaximumLength(100);
        RuleFor(x => x.Role).NotEmpty().MaximumLength(50);
        RuleFor(x => x.Barcode).NotEmpty().MaximumLength(100);
        // Same contract as SetPinValidator — the credential must be usable by
        // the kiosk-login PIN pad.
        RuleFor(x => x.Pin).NotEmpty().Length(4, 8)
            .Matches(@"^\d+$").WithMessage("PIN must contain only digits");
    }
}

public class CreateKioskUserHandler(
    UserManager<ApplicationUser> userManager,
    RoleManager<IdentityRole<int>> roleManager,
    AppDbContext db)
    : IRequestHandler<CreateKioskUserCommand, CreateKioskUserResponseModel>
{
    public async Task<CreateKioskUserResponseModel> Handle(
        CreateKioskUserCommand request, CancellationToken cancellationToken)
    {
        // Barcode is the kiosk-login lookup key — enforce uniqueness up front
        // for a clean 409 instead of a second identity silently shadowing the
        // first at the scan station.
        var barcodeTaken = await db.Users.AnyAsync(
            u => u.EmployeeBarcode == request.Barcode, cancellationToken);
        if (barcodeTaken)
            throw new InvalidOperationException(
                $"Barcode '{request.Barcode}' is already assigned to another user.");

        if (!await roleManager.RoleExistsAsync(request.Role))
            throw new InvalidOperationException($"Role '{request.Role}' does not exist.");

        var user = new ApplicationUser
        {
            UserName = request.Email,
            Email = request.Email,
            FirstName = request.FirstName,
            LastName = request.LastName,
            Initials = CreateAdminUserHandler.GenerateInitials(request.FirstName, request.LastName),
            AvatarColor = "#94a3b8",
            EmailConfirmed = true,
            EmployeeBarcode = request.Barcode,
            // Reuse SetPinHandler's PBKDF2 path (not a copy of it) so the
            // credential verifies against KioskLoginHandler exactly like a
            // self-set PIN.
            PinHash = SetPinHandler.HashPin(request.Pin),
        };

        var result = await userManager.CreateAsync(user);
        if (!result.Succeeded)
        {
            var errors = string.Join("; ", result.Errors.Select(e => e.Description));
            throw new InvalidOperationException($"Failed to create user: {errors}");
        }

        await userManager.AddToRoleAsync(user, request.Role);

        db.LogActivityAt("created",
            $"Kiosk identity provisioned: {request.Email}, role {request.Role}, barcode {request.Barcode}",
            ("ApplicationUser", user.Id));
        await db.SaveChangesAsync(cancellationToken);

        return new CreateKioskUserResponseModel(
            user.Id, user.Email!, user.FirstName, user.LastName,
            request.Role, request.Barcode, user.CreatedAt);
    }
}
