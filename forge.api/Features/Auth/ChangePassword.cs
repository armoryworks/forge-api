using FluentValidation;
using MediatR;
using Microsoft.AspNetCore.Identity;

using Forge.Api.Services;
using Forge.Core.Interfaces;
using Forge.Data.Context;

namespace Forge.Api.Features.Auth;

public record ChangePasswordCommand(
    int UserId,
    string CurrentPassword,
    string NewPassword) : IRequest;

public class ChangePasswordValidator : AbstractValidator<ChangePasswordCommand>
{
    public ChangePasswordValidator()
    {
        RuleFor(x => x.CurrentPassword)
            .NotEmpty().WithMessage("Current password is required.");

        RuleFor(x => x.NewPassword)
            .NotEmpty().WithMessage("New password is required.")
            .MinimumLength(8).WithMessage("New password must be at least 8 characters.");
    }
}

public class ChangePasswordHandler(
    UserManager<ApplicationUser> userManager,
    ISessionStore sessionStore,
    ISystemAuditWriter auditWriter)
    : IRequestHandler<ChangePasswordCommand>
{
    public async Task Handle(ChangePasswordCommand request, CancellationToken cancellationToken)
    {
        var user = await userManager.FindByIdAsync(request.UserId.ToString())
            ?? throw new KeyNotFoundException("User not found");

        var isCurrentValid = await userManager.CheckPasswordAsync(user, request.CurrentPassword);

        if (!isCurrentValid)
            throw new UnauthorizedAccessException("Current password is incorrect");

        var result = await userManager.ChangePasswordAsync(user, request.CurrentPassword, request.NewPassword);

        if (!result.Succeeded)
        {
            var errors = string.Join("; ", result.Errors.Select(e => e.Description));
            throw new InvalidOperationException($"Password change failed: {errors}");
        }

        user.UpdatedAt = DateTimeOffset.UtcNow;
        await userManager.UpdateAsync(user);

        // Revoke all existing sessions — user must re-authenticate with new password
        await sessionStore.RevokeAllUserSessionsAsync(request.UserId, cancellationToken);

        await auditWriter.WriteAsync("UserPasswordChanged", request.UserId,
            entityType: "ApplicationUser",
            entityId: request.UserId,
            ct: cancellationToken);
    }
}
