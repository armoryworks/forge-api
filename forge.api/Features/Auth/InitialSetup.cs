using FluentValidation;
using MediatR;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

using Forge.Api.Data;
using Forge.Core.Entities;
using Forge.Core.Interfaces;
using Forge.Data.Context;

namespace Forge.Api.Features.Auth;

public record InitialSetupCommand(
    string Email,
    string Password,
    string FirstName,
    string LastName,
    // Company profile (optional)
    string? CompanyName,
    string? CompanyPhone,
    string? CompanyEmail,
    string? CompanyEin,
    string? CompanyWebsite,
    // Primary location (optional)
    string? LocationName,
    string? LocationLine1,
    string? LocationLine2,
    string? LocationCity,
    string? LocationState,
    string? LocationPostalCode) : IRequest<LoginResponse>;

public class InitialSetupValidator : AbstractValidator<InitialSetupCommand>
{
    public InitialSetupValidator()
    {
        RuleFor(x => x.Email)
            .NotEmpty().WithMessage("Email is required.")
            .EmailAddress().WithMessage("A valid email address is required.");

        RuleFor(x => x.Password)
            .NotEmpty().WithMessage("Password is required.")
            .MinimumLength(8).WithMessage("Password must be at least 8 characters.");

        RuleFor(x => x.FirstName)
            .NotEmpty().WithMessage("First name is required.");

        RuleFor(x => x.LastName)
            .NotEmpty().WithMessage("Last name is required.");
    }
}

public class InitialSetupHandler(
    UserManager<ApplicationUser> userManager,
    RoleManager<IdentityRole<int>> roleManager,
    AppDbContext db,
    ITokenService tokenService,
    ISessionStore sessionStore,
    IHttpContextAccessor httpContext)
    : IRequestHandler<InitialSetupCommand, LoginResponse>
{
    public async Task<LoginResponse> Handle(InitialSetupCommand request, CancellationToken cancellationToken)
    {
        if (userManager.Users.Any())
            throw new InvalidOperationException("Setup has already been completed.");

        // Create roles
        string[] roles = ["Admin", "Manager", "Engineer", "PM", "ProductionWorker", "OfficeManager"];
        foreach (var role in roles)
        {
            if (!await roleManager.RoleExistsAsync(role))
                await roleManager.CreateAsync(new IdentityRole<int>(role));
        }

        // Create admin user
        var initials = $"{request.FirstName[..1].ToUpper()}{request.LastName[..1].ToUpper()}";
        var user = new ApplicationUser
        {
            UserName = request.Email,
            Email = request.Email,
            FirstName = request.FirstName.Trim(),
            LastName = request.LastName.Trim(),
            Initials = initials,
            AvatarColor = "#0d9488",
            EmailConfirmed = true,
        };

        var createResult = await userManager.CreateAsync(user, request.Password);
        if (!createResult.Succeeded)
        {
            var errors = string.Join("; ", createResult.Errors.Select(e => e.Description));
            throw new InvalidOperationException($"Failed to create account: {errors}");
        }

        await userManager.AddToRoleAsync(user, "Admin");

        // Seed essential operational data
        await SeedData.SeedEssentialDataAsync(db);

        // Save company profile settings if provided
        if (!string.IsNullOrWhiteSpace(request.CompanyName))
        {
            var settingMap = new Dictionary<string, string>
            {
                ["company.name"] = request.CompanyName.Trim(),
                ["company.phone"] = request.CompanyPhone?.Trim() ?? "",
                ["company.email"] = request.CompanyEmail?.Trim() ?? "",
                ["company.ein"] = request.CompanyEin?.Trim() ?? "",
                ["company.website"] = request.CompanyWebsite?.Trim() ?? "",
            };

            foreach (var (key, value) in settingMap)
            {
                var setting = await db.SystemSettings.FirstOrDefaultAsync(s => s.Key == key, cancellationToken);
                if (setting != null)
                    setting.Value = value;
            }

            // Also set the display name
            var appName = await db.SystemSettings.FirstOrDefaultAsync(s => s.Key == "app.company_name", cancellationToken);
            if (appName != null)
                appName.Value = request.CompanyName.Trim();

            await db.SaveChangesAsync(cancellationToken);
        }

        // Create primary location if address provided
        if (!string.IsNullOrWhiteSpace(request.LocationLine1))
        {
            var location = new CompanyLocation
            {
                Name = request.LocationName?.Trim() ?? "Main Office",
                Line1 = request.LocationLine1.Trim(),
                Line2 = request.LocationLine2?.Trim(),
                City = request.LocationCity?.Trim() ?? "",
                State = request.LocationState?.Trim() ?? "",
                PostalCode = request.LocationPostalCode?.Trim() ?? "",
                Country = "US",
                IsDefault = true,
                IsActive = true,
            };
            db.CompanyLocations.Add(location);
            await db.SaveChangesAsync(cancellationToken);

            // Assign admin to this location
            user.WorkLocationId = location.Id;
            await userManager.UpdateAsync(user);
        }

        // Generate JWT
        var userRoles = await userManager.GetRolesAsync(user);
        var result = tokenService.GenerateToken(
            user.Id, user.Email!, user.FirstName, user.LastName,
            user.Initials, user.AvatarColor, userRoles);

        await sessionStore.CreateSessionAsync(user.Id, result.Jti, result.ExpiresAt,
            "setup",
            httpContext.HttpContext?.Connection.RemoteIpAddress?.ToString(),
            httpContext.HttpContext?.Request.Headers.UserAgent.ToString(),
            cancellationToken);

        var userResponse = new AuthUserResponseModel(
            user.Id, user.Email!, user.FirstName, user.LastName,
            user.Initials, user.AvatarColor, userRoles.ToArray(), false);

        return new LoginResponse(result.Token, result.ExpiresAt, userResponse);
    }
}
