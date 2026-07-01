using FluentValidation;
using MediatR;
using Microsoft.AspNetCore.Identity;

using Forge.Api.Services;
using Forge.Core.Interfaces;
using Forge.Data.Context;

namespace Forge.Api.Features.Auth;

public record ChangeEmailCommand(
    int UserId,
    string CurrentPassword,
    string NewEmail) : IRequest;

public class ChangeEmailValidator : AbstractValidator<ChangeEmailCommand>
{
    public ChangeEmailValidator()
    {
        RuleFor(x => x.CurrentPassword)
            .NotEmpty().WithMessage("Current password is required.");

        RuleFor(x => x.NewEmail)
            .NotEmpty().WithMessage("New email address is required.")
            .EmailAddress().WithMessage("A valid email address is required.")
            .MaximumLength(256).WithMessage("Email address must be 256 characters or fewer.");
    }
}

public class ChangeEmailHandler(
    UserManager<ApplicationUser> userManager,
    ISessionStore sessionStore,
    ISystemAuditWriter auditWriter)
    : IRequestHandler<ChangeEmailCommand>
{
    public async Task Handle(ChangeEmailCommand request, CancellationToken cancellationToken)
    {
        var user = await userManager.FindByIdAsync(request.UserId.ToString())
            ?? throw new KeyNotFoundException("User not found");

        var isCurrentValid = await userManager.CheckPasswordAsync(user, request.CurrentPassword);

        if (!isCurrentValid)
            throw new UnauthorizedAccessException("Current password is incorrect");

        var previousEmail = user.Email;

        // No-op guard — reject if the requested email is the one already on the account.
        if (string.Equals(request.NewEmail, previousEmail, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("That is already your email address.");

        // Uniqueness — Email is the login identity (RequireUniqueEmail = true), so a
        // collision would break login. Pre-check for a clean 409 with a readable message.
        var existing = await userManager.FindByEmailAsync(request.NewEmail);
        if (existing is not null && existing.Id != user.Id)
            throw new InvalidOperationException("That email address is already in use.");

        // Email is the login identity — UserName is kept in sync with Email at account
        // creation (see InitialSetup / CreateAdminUser / GrantSystemAccess), so any change
        // must update both. SetEmailAsync re-normalizes NormalizedEmail; SetUserNameAsync
        // re-normalizes NormalizedUserName.
        var emailResult = await userManager.SetEmailAsync(user, request.NewEmail);
        if (!emailResult.Succeeded)
        {
            var errors = string.Join("; ", emailResult.Errors.Select(e => e.Description));
            throw new InvalidOperationException($"Email change failed: {errors}");
        }

        var userNameResult = await userManager.SetUserNameAsync(user, request.NewEmail);
        if (!userNameResult.Succeeded)
        {
            var errors = string.Join("; ", userNameResult.Errors.Select(e => e.Description));
            throw new InvalidOperationException($"Email change failed: {errors}");
        }

        user.UpdatedAt = DateTimeOffset.UtcNow;
        await userManager.UpdateAsync(user);

        // Revoke all existing sessions — user must re-authenticate with the new email
        await sessionStore.RevokeAllUserSessionsAsync(request.UserId, cancellationToken);

        // Email is the login credential — surface the change as a discrete, high-signal
        // system-wide audit row (same action string the admin email-change uses).
        await auditWriter.WriteAsync("UserEmailChanged", request.UserId,
            entityType: "ApplicationUser",
            entityId: request.UserId,
            details: System.Text.Json.JsonSerializer.Serialize(new
            {
                targetUserId = request.UserId,
                fromEmail = previousEmail,
                toEmail = user.Email,
            }),
            ct: cancellationToken);
    }
}
