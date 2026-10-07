using FluentValidation;
using MediatR;
using Microsoft.AspNetCore.Identity;

using Forge.Api.Capabilities;
using Forge.Api.Features.EmployeeProfile;
using Forge.Data.Context;

namespace Forge.Api.Features.Auth;

public record UpdateProfileCommand(
    int UserId,
    string FirstName,
    string LastName,
    string? Initials,
    string? AvatarColor) : IRequest<AuthUserResponseModel>;

public class UpdateProfileValidator : AbstractValidator<UpdateProfileCommand>
{
    public UpdateProfileValidator()
    {
        RuleFor(x => x.FirstName)
            .NotEmpty().WithMessage("First name is required.")
            .MaximumLength(100).WithMessage("First name must not exceed 100 characters.");

        RuleFor(x => x.LastName)
            .NotEmpty().WithMessage("Last name is required.")
            .MaximumLength(100).WithMessage("Last name must not exceed 100 characters.");

        RuleFor(x => x.Initials)
            .MaximumLength(4).WithMessage("Initials must not exceed 4 characters.")
            .When(x => x.Initials is not null);

        RuleFor(x => x.AvatarColor)
            .MaximumLength(20).WithMessage("Avatar color must not exceed 20 characters.")
            .When(x => x.AvatarColor is not null);
    }
}

public class UpdateProfileHandler(
    UserManager<ApplicationUser> userManager,
    AppDbContext db,
    ICapabilitySnapshotProvider capabilities)
    : IRequestHandler<UpdateProfileCommand, AuthUserResponseModel>
{
    public async Task<AuthUserResponseModel> Handle(UpdateProfileCommand request, CancellationToken cancellationToken)
    {
        var user = await userManager.FindByIdAsync(request.UserId.ToString())
            ?? throw new KeyNotFoundException("User not found");

        user.FirstName = request.FirstName;
        user.LastName = request.LastName;
        user.Initials = request.Initials;
        user.AvatarColor = request.AvatarColor;
        user.UpdatedAt = DateTimeOffset.UtcNow;

        await userManager.UpdateAsync(user);

        var roles = await userManager.GetRolesAsync(user);

        var profileComplete = await EmployeeComplianceRules.IsProfileCompleteAsync(db, capabilities, user.Id, cancellationToken);

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
