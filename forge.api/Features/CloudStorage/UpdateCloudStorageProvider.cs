using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;

using Forge.Api.Capabilities;
using Forge.Core.Interfaces;
using Forge.Core.Models;
using Forge.Data.Context;

namespace Forge.Api.Features.CloudStorage;

public record UpdateCloudStorageProviderCommand(int Id, UpdateCloudStorageProviderRequestModel Model) : IRequest<CloudStorageProviderResponseModel>;

public class UpdateCloudStorageProviderValidator : AbstractValidator<UpdateCloudStorageProviderCommand>
{
    public UpdateCloudStorageProviderValidator()
    {
        RuleFor(x => x.Model.Mode).IsInEnum();
        RuleFor(x => x.Model.RootFolderId).MaximumLength(500);
        RuleFor(x => x.Model.ServiceAccountId).MaximumLength(500);
        RuleFor(x => x.Model.AccessToken).MaximumLength(2000);
        RuleFor(x => x.Model.RefreshToken).MaximumLength(2000);
    }
}

public class UpdateCloudStorageProviderHandler(
    AppDbContext db,
    ITokenEncryptionService encryption,
    ICapabilitySnapshotProvider capabilities) : IRequestHandler<UpdateCloudStorageProviderCommand, CloudStorageProviderResponseModel>
{
    public async Task<CloudStorageProviderResponseModel> Handle(UpdateCloudStorageProviderCommand request, CancellationToken ct)
    {
        var provider = await db.CloudStorageProviders.FirstOrDefaultAsync(p => p.Id == request.Id, ct)
            ?? throw new KeyNotFoundException($"Cloud storage provider {request.Id} not found.");
        CloudStorageProviderGuards.EnsureCapability(provider.ProviderCode, capabilities);

        var model = request.Model;
        if (model.IsActive && !provider.IsActive)
            await CloudStorageProviderGuards.EnsureNoOtherActiveAsync(db, provider.ProviderCode, provider.Id, ct);

        provider.Mode = model.Mode;
        provider.IsActive = model.IsActive;
        provider.RootFolderId = string.IsNullOrWhiteSpace(model.RootFolderId) ? null : model.RootFolderId.Trim();
        provider.ServiceAccountId = string.IsNullOrWhiteSpace(model.ServiceAccountId) ? null : model.ServiceAccountId.Trim();
        if (!string.IsNullOrEmpty(model.AccessToken))
        {
            provider.OAuthTokenEncrypted = encryption.Encrypt(model.AccessToken);
            provider.TokenExpiresAt = model.TokenExpiresAt;
            provider.LastError = null;
        }
        if (!string.IsNullOrEmpty(model.RefreshToken))
            provider.RefreshTokenEncrypted = encryption.Encrypt(model.RefreshToken);

        await db.SaveChangesAsync(ct);

        var linked = await db.EntityCloudLinks.CountAsync(l => l.ProviderId == provider.Id, ct);
        return CloudStorageProviderCodes.ToModel(provider, linked);
    }
}
