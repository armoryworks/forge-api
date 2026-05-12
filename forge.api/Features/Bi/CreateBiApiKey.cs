using System.Security.Cryptography;
using System.Text.Json;

using FluentValidation;
using MediatR;
using Microsoft.AspNetCore.Identity;

using Forge.Api.Services;
using Forge.Core.Entities;
using Forge.Core.Models;
using Forge.Data.Context;

namespace Forge.Api.Features.Bi;

public record CreateBiApiKeyCommand(CreateBiApiKeyRequestModel Model) : IRequest<CreateBiApiKeyResponseModel>;

public class CreateBiApiKeyValidator : AbstractValidator<CreateBiApiKeyCommand>
{
    public CreateBiApiKeyValidator()
    {
        RuleFor(x => x.Model.Name).NotEmpty().MaximumLength(200);
    }
}

public class CreateBiApiKeyHandler(AppDbContext db, ISystemAuditWriter auditWriter)
    : IRequestHandler<CreateBiApiKeyCommand, CreateBiApiKeyResponseModel>
{
    public async Task<CreateBiApiKeyResponseModel> Handle(
        CreateBiApiKeyCommand request, CancellationToken cancellationToken)
    {
        var model = request.Model;

        // Generate a random API key
        var keyBytes = RandomNumberGenerator.GetBytes(32);
        var plaintextKey = $"qbe_{Convert.ToBase64String(keyBytes).Replace("+", "").Replace("/", "").Replace("=", "")}";
        var keyPrefix = plaintextKey[..12];

        // Hash the key for storage
        var hasher = new PasswordHasher<object>();
        var keyHash = hasher.HashPassword(null!, plaintextKey);

        var apiKey = new BiApiKey
        {
            Name = model.Name,
            KeyHash = keyHash,
            KeyPrefix = keyPrefix,
            ExpiresAt = model.ExpiresAt,
            AllowedEntitySetsJson = model.AllowedEntitySets != null
                ? JsonSerializer.Serialize(model.AllowedEntitySets) : null,
            AllowedIpsJson = model.AllowedIps != null
                ? JsonSerializer.Serialize(model.AllowedIps) : null,
        };

        db.BiApiKeys.Add(apiKey);
        await db.SaveChangesAsync(cancellationToken);

        // System-wide audit row. Phase 3 / WU-04 / A3.
        // NEVER include the plaintext or hash in details — name + prefix only.
        var actorId = db.CurrentUserId ?? 0;
        var details = JsonSerializer.Serialize(new
        {
            name = apiKey.Name,
            keyPrefix = apiKey.KeyPrefix,
            expiresAt = apiKey.ExpiresAt,
            allowedEntitySets = model.AllowedEntitySets,
            allowedIps = model.AllowedIps,
        });
        await auditWriter.WriteAsync(
            action: "BiApiKeyIssued",
            userId: actorId,
            entityType: nameof(BiApiKey),
            entityId: apiKey.Id,
            details: details,
            ct: cancellationToken);

        return new CreateBiApiKeyResponseModel
        {
            Id = apiKey.Id,
            Name = apiKey.Name,
            KeyPrefix = apiKey.KeyPrefix,
            PlaintextKey = plaintextKey,
            ExpiresAt = apiKey.ExpiresAt,
        };
    }
}
