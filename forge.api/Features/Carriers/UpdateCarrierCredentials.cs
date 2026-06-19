using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;

using Forge.Core.Interfaces;
using Forge.Data.Context;
using Forge.Data.Extensions;

namespace Forge.Api.Features.Carriers;

public record UpdateCarrierCredentialsCommand(
    int CarrierId, string ClientId, string Secret, string? AccountNumber, string Environment) : IRequest;

public class UpdateCarrierCredentialsValidator : AbstractValidator<UpdateCarrierCredentialsCommand>
{
    public UpdateCarrierCredentialsValidator()
    {
        RuleFor(x => x.CarrierId).GreaterThan(0);
        RuleFor(x => x.ClientId).NotEmpty().MaximumLength(200);
        RuleFor(x => x.Secret).NotEmpty();
        RuleFor(x => x.AccountNumber).MaximumLength(50);
        RuleFor(x => x.Environment)
            .Must(e => e is "sandbox" or "production")
            .WithMessage("Environment must be 'sandbox' or 'production'.");
    }
}

public class UpdateCarrierCredentialsHandler(AppDbContext db, ITokenEncryptionService encryption)
    : IRequestHandler<UpdateCarrierCredentialsCommand>
{
    public async Task Handle(UpdateCarrierCredentialsCommand request, CancellationToken cancellationToken)
    {
        var carrier = await db.Carriers.FirstOrDefaultAsync(c => c.Id == request.CarrierId, cancellationToken)
            ?? throw new KeyNotFoundException($"Carrier {request.CarrierId} not found");

        carrier.CredentialClientId = request.ClientId.Trim();
        // Encrypt at rest — the plaintext secret never persists and is never returned by the API.
        carrier.CredentialSecret = encryption.Encrypt(request.Secret);
        carrier.CredentialAccountNumber = string.IsNullOrWhiteSpace(request.AccountNumber)
            ? null : request.AccountNumber.Trim();
        carrier.CredentialEnvironment = request.Environment.ToLowerInvariant();

        // Audit the change WITHOUT the secret.
        db.LogActivityAt("credentials-updated",
            $"API credentials updated for {carrier.Name} ({carrier.CredentialEnvironment})",
            ("Carrier", carrier.Id));

        await db.SaveChangesAsync(cancellationToken);
    }
}
