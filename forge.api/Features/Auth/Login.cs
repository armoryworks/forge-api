using System.Security.Claims;

using FluentValidation;
using MediatR;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

using Forge.Api.Services;
using Forge.Core.Interfaces;
using Forge.Data.Context;

namespace Forge.Api.Features.Auth;

public record AuthUserResponseModel(
    int Id,
    string Email,
    string FirstName,
    string LastName,
    string? Initials,
    string? AvatarColor,
    string[] Roles,
    bool ProfileComplete);

// --- Login ---

public record LoginCommand(string Email, string Password) : IRequest<LoginResponse>;

public record LoginResponse(string Token, DateTimeOffset ExpiresAt, AuthUserResponseModel User, bool MfaRequired = false, int? MfaUserId = null);

public class LoginValidator : AbstractValidator<LoginCommand>
{
    public LoginValidator()
    {
        RuleFor(x => x.Email)
            .NotEmpty().WithMessage("Email is required.")
            .EmailAddress().WithMessage("A valid email address is required.");

        RuleFor(x => x.Password)
            .NotEmpty().WithMessage("Password is required.");
    }
}

public class LoginHandler(
    UserManager<ApplicationUser> userManager,
    ITokenService tokenService,
    ISessionStore sessionStore,
    IHttpContextAccessor httpContext,
    AppDbContext db,
    ISystemAuditWriter auditWriter,
    IRoleClaimsExpander roleClaimsExpander)
    : IRequestHandler<LoginCommand, LoginResponse>
{
    public async Task<LoginResponse> Handle(LoginCommand request, CancellationToken cancellationToken)
    {
        var user = await userManager.FindByEmailAsync(request.Email);

        if (user is null || !user.IsActive)
        {
            // Audit failed login (cross-entity event). UserId is the matched
            // user's id when known by email (even if inactive), 0 otherwise.
            var attemptedUserId = user?.Id ?? 0;
            var failDetails = System.Text.Json.JsonSerializer.Serialize(new
            {
                email = request.Email,
                reason = user is null ? "user-not-found" : "user-inactive",
            });
            await auditWriter.WriteAsync("UserLoginFailed", attemptedUserId,
                entityType: "ApplicationUser",
                entityId: user?.Id,
                details: failDetails,
                ct: cancellationToken);
            throw new UnauthorizedAccessException("Invalid credentials");
        }

        var passwordValid = await userManager.CheckPasswordAsync(user, request.Password);

        if (!passwordValid)
        {
            await auditWriter.WriteAsync("UserLoginFailed", user.Id,
                entityType: "ApplicationUser",
                entityId: user.Id,
                details: System.Text.Json.JsonSerializer.Serialize(new
                {
                    email = request.Email,
                    reason = "invalid-password",
                }),
                ct: cancellationToken);
            throw new UnauthorizedAccessException("Invalid credentials");
        }

        // Check if MFA is required
        if (user.MfaEnabled)
        {
            return new LoginResponse(
                Token: string.Empty,
                ExpiresAt: DateTimeOffset.MinValue,
                User: null!,
                MfaRequired: true,
                MfaUserId: user.Id);
        }

        // WU-06 / C1 — combine identity roles with any roles included via
        // the user's assigned RoleTemplate so the JWT carries the full set.
        var roles = await roleClaimsExpander.GetEffectiveRolesAsync(user, cancellationToken);

        var result = tokenService.GenerateToken(
            user.Id, user.Email!, user.FirstName, user.LastName,
            user.Initials, user.AvatarColor, roles);

        await sessionStore.CreateSessionAsync(user.Id, result.Jti, result.ExpiresAt,
            "credentials",
            httpContext.HttpContext?.Connection.RemoteIpAddress?.ToString(),
            httpContext.HttpContext?.Request.Headers.UserAgent.ToString(),
            cancellationToken);

        var profileComplete = await CheckProfileComplete(user.Id, cancellationToken);

        await auditWriter.WriteAsync("UserLoggedIn", user.Id,
            entityType: "ApplicationUser",
            entityId: user.Id,
            details: System.Text.Json.JsonSerializer.Serialize(new
            {
                email = user.Email,
                method = "credentials",
            }),
            ct: cancellationToken);

        var userResponse = new AuthUserResponseModel(
            user.Id,
            user.Email!,
            user.FirstName,
            user.LastName,
            user.Initials,
            user.AvatarColor,
            roles.ToArray(),
            profileComplete);

        return new LoginResponse(result.Token, result.ExpiresAt, userResponse);
    }

    private async Task<bool> CheckProfileComplete(int userId, CancellationToken ct)
    {
        var profile = await db.EmployeeProfiles
            .AsNoTracking()
            .FirstOrDefaultAsync(p => p.UserId == userId, ct);

        if (profile is null) return false;
        if (profile.OnboardingBypassedAt is not null) return true;

        return !string.IsNullOrWhiteSpace(profile.Street1) &&
               !string.IsNullOrWhiteSpace(profile.City) &&
               !string.IsNullOrWhiteSpace(profile.State) &&
               !string.IsNullOrWhiteSpace(profile.ZipCode) &&
               !string.IsNullOrWhiteSpace(profile.EmergencyContactName) &&
               !string.IsNullOrWhiteSpace(profile.EmergencyContactPhone) &&
               profile.W4CompletedAt is not null &&
               profile.I9CompletedAt is not null &&
               profile.StateWithholdingCompletedAt is not null &&
               profile.DirectDepositCompletedAt is not null &&
               profile.WorkersCompAcknowledgedAt is not null &&
               profile.HandbookAcknowledgedAt is not null;
    }
}

// --- Get Current User ---

public record GetCurrentUserQuery(ClaimsPrincipal User) : IRequest<AuthUserResponseModel>;

public class GetCurrentUserHandler(
    UserManager<ApplicationUser> userManager,
    AppDbContext db,
    IRoleClaimsExpander roleClaimsExpander)
    : IRequestHandler<GetCurrentUserQuery, AuthUserResponseModel>
{
    public async Task<AuthUserResponseModel> Handle(GetCurrentUserQuery request, CancellationToken cancellationToken)
    {
        var userId = request.User.FindFirstValue(ClaimTypes.NameIdentifier)
            ?? throw new UnauthorizedAccessException("Invalid credentials");

        var user = await userManager.FindByIdAsync(userId)
            ?? throw new UnauthorizedAccessException("Invalid credentials");

        if (!user.IsActive)
            throw new UnauthorizedAccessException("Invalid credentials");

        // WU-06 / C1 — same expansion as login so /auth/me reflects rollups.
        var roles = await roleClaimsExpander.GetEffectiveRolesAsync(user, cancellationToken);

        var profile = await db.EmployeeProfiles
            .AsNoTracking()
            .FirstOrDefaultAsync(p => p.UserId == user.Id, cancellationToken);

        var profileComplete = profile is not null && (
            profile.OnboardingBypassedAt is not null ||
            (!string.IsNullOrWhiteSpace(profile.Street1) &&
             !string.IsNullOrWhiteSpace(profile.City) &&
             !string.IsNullOrWhiteSpace(profile.State) &&
             !string.IsNullOrWhiteSpace(profile.ZipCode) &&
             !string.IsNullOrWhiteSpace(profile.EmergencyContactName) &&
             !string.IsNullOrWhiteSpace(profile.EmergencyContactPhone) &&
             profile.W4CompletedAt is not null &&
             profile.I9CompletedAt is not null &&
             profile.StateWithholdingCompletedAt is not null &&
             profile.DirectDepositCompletedAt is not null &&
             profile.WorkersCompAcknowledgedAt is not null &&
             profile.HandbookAcknowledgedAt is not null));

        return new AuthUserResponseModel(
            user.Id,
            user.Email!,
            user.FirstName,
            user.LastName,
            user.Initials,
            user.AvatarColor,
            roles.ToArray(),
            profileComplete);
    }
}
