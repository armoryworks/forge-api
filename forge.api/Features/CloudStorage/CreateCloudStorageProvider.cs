using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;

using Forge.Api.Capabilities;
using Forge.Core.Entities;
using Forge.Core.Enums;
using Forge.Core.Interfaces;
using Forge.Core.Models;
using Forge.Data.Context;

namespace Forge.Api.Features.CloudStorage;

public record CreateCloudStorageProviderCommand(CreateCloudStorageProviderRequestModel Model) : IRequest<CloudStorageProviderResponseModel>;

public class CreateCloudStorageProviderValidator : AbstractValidator<CreateCloudStorageProviderCommand>
{
    public CreateCloudStorageProviderValidator()
    {
        RuleFor(x => x.Model.ProviderCode)
            .NotEmpty()
            .Must(c => CloudStorageProviderCodes.CapabilityByCode.ContainsKey(c.Trim()))
            .WithMessage($"Provider must be one of: {string.Join(", ", CloudStorageProviderCodes.CapabilityByCode.Keys)}.")
            .WithErrorCode("cloud-storage-provider-unknown");
        RuleFor(x => x.Model.Mode).IsInEnum();
        RuleFor(x => x.Model.RootFolderId).MaximumLength(500);
        RuleFor(x => x.Model.ServiceAccountId).MaximumLength(500);
        RuleFor(x => x.Model.AccessToken).MaximumLength(2000);
        RuleFor(x => x.Model.RefreshToken).MaximumLength(2000);
        RuleFor(x => x.Model.AccessToken).NotEmpty()
            .When(x => x.Model.Mode == CloudStorageProviderMode.ServiceAccount && !IsMock(x.Model.ProviderCode))
            .WithMessage("A service-account provider needs an access token.");
        RuleFor(x => x.Model.RefreshToken).NotEmpty()
            .When(x => x.Model.Mode == CloudStorageProviderMode.ServiceAccount && !IsMock(x.Model.ProviderCode))
            .WithMessage("A service-account provider needs a refresh token.");
    }

    private static bool IsMock(string? code) =>
        string.Equals(code?.Trim(), CloudStorageProviderCodes.Mock, StringComparison.OrdinalIgnoreCase);
}

public class CreateCloudStorageProviderHandler(
    AppDbContext db,
    ITokenEncryptionService encryption,
    ICapabilitySnapshotProvider capabilities) : IRequestHandler<CreateCloudStorageProviderCommand, CloudStorageProviderResponseModel>
{
    public async Task<CloudStorageProviderResponseModel> Handle(CreateCloudStorageProviderCommand request, CancellationToken ct)
    {
        var model = request.Model;
        var code = CloudStorageProviderCodes.Normalize(model.ProviderCode);
        CloudStorageProviderGuards.EnsureCapability(code, capabilities);

        if (model.IsActive)
            await CloudStorageProviderGuards.EnsureNoOtherActiveAsync(db, code, null, ct);

        var provider = new CloudStorageProvider
        {
            ProviderCode = code,
            Mode = model.Mode,
            IsActive = model.IsActive,
            RootFolderId = Blank(model.RootFolderId),
            ServiceAccountId = Blank(model.ServiceAccountId),
            TokenExpiresAt = model.TokenExpiresAt,
        };
        if (!string.IsNullOrEmpty(model.AccessToken))
            provider.OAuthTokenEncrypted = encryption.Encrypt(model.AccessToken);
        if (!string.IsNullOrEmpty(model.RefreshToken))
            provider.RefreshTokenEncrypted = encryption.Encrypt(model.RefreshToken);

        db.CloudStorageProviders.Add(provider);
        await db.SaveChangesAsync(ct);
        return CloudStorageProviderCodes.ToModel(provider, 0);
    }

    private static string? Blank(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
