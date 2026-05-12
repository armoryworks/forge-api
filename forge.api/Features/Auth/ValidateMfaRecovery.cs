using FluentValidation;
using MediatR;

using Forge.Core.Interfaces;
using Forge.Core.Models;

namespace Forge.Api.Features.Auth;

public record ValidateMfaRecoveryCommand(string ChallengeToken, string RecoveryCode) : IRequest<MfaValidateResponseModel?>;

public class ValidateMfaRecoveryValidator : AbstractValidator<ValidateMfaRecoveryCommand>
{
    public ValidateMfaRecoveryValidator()
    {
        RuleFor(x => x.ChallengeToken).NotEmpty();
        RuleFor(x => x.RecoveryCode).NotEmpty();
    }
}

public class ValidateMfaRecoveryHandler(IMfaService mfaService) : IRequestHandler<ValidateMfaRecoveryCommand, MfaValidateResponseModel?>
{
    public async Task<MfaValidateResponseModel?> Handle(ValidateMfaRecoveryCommand request, CancellationToken cancellationToken)
    {
        return await mfaService.ValidateRecoveryCodeAsync(request.ChallengeToken, request.RecoveryCode, cancellationToken);
    }
}
