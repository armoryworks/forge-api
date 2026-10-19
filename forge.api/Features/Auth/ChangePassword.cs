using FluentValidation;
using FluentValidation.Results;
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
            .MinimumLength(8).WithMessage("New password must be at least 8 characters.")
            .Matches("[A-Z]").WithMessage("New password must contain an uppercase letter.")
            .Matches("[a-z]").WithMessage("New password must contain a lowercase letter.")
            .Matches("[0-9]").WithMessage("New password must contain a digit.")
            .NotEqual(x => x.CurrentPassword).WithMessage("New password must be different from the current password.");
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
            throw new ValidationException([
                new ValidationFailure(nameof(request.CurrentPassword), "Current password is incorrect."),
            ]);

        var result = await userManager.ChangePasswordAsync(user, request.CurrentPassword, request.NewPassword);

        if (!result.Succeeded)
            throw new ValidationException(result.Errors
                .Select(e => new ValidationFailure(nameof(request.NewPassword), e.Description)));

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
