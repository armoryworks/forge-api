using System.Security.Cryptography;
using System.Text.Json;

using FluentValidation;
using MediatR;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

using Forge.Api.Services;
using Forge.Core.Entities;
using Forge.Core.Models;
using Forge.Data.Context;

namespace Forge.Api.Features.SystemApiKeys;

public record CreateSystemApiKeyCommand(CreateSystemApiKeyRequestModel Model)
    : IRequest<CreateSystemApiKeyResponseModel>;

public class CreateSystemApiKeyValidator : AbstractValidator<CreateSystemApiKeyCommand>
{
    public CreateSystemApiKeyValidator()
    {
        RuleFor(x => x.Model.Name).NotEmpty().MaximumLength(200);
        RuleFor(x => x.Model.UserId).GreaterThan(0)
            .WithMessage("UserId is required — system API keys are user-bound.");
    }
}

/// <summary>
/// Issues a user-bound system API key. The plaintext is returned ONCE in
/// the response and never persisted (only the PBKDF2 hash + 12-char prefix
/// are stored).
///
/// Key format: <c>fsk_&lt;base64url(32 random bytes)&gt;</c>. The <c>fsk_</c>
/// prefix (Forge System Key) distinguishes these from BI keys (<c>qbe_</c>)
/// at a glance in logs / secrets stores. The full 12-char prefix
/// (<c>fsk_xxxxxxxx</c>) is the lookup index used by the auth handler.
/// </summary>
public class CreateSystemApiKeyHandler(AppDbContext db, ISystemAuditWriter auditWriter)
    : IRequestHandler<CreateSystemApiKeyCommand, CreateSystemApiKeyResponseModel>
{
    public async Task<CreateSystemApiKeyResponseModel> Handle(
        CreateSystemApiKeyCommand request, CancellationToken cancellationToken)
    {
        var model = request.Model;

        // Verify the bound user exists and is active — refuse to issue a
        // key that cannot authenticate.
        var userExists = await db.Users
            .AnyAsync(u => u.Id == model.UserId && u.IsActive, cancellationToken);
        if (!userExists)
            throw new KeyNotFoundException(
                $"ApplicationUser {model.UserId} not found or not active.");

        // If a role-template binding is requested, verify it exists and is
        // active. Refuse rather than silently dropping the binding — an
        // admin issuing a key with explicit scoping intent shouldn't have it
        // quietly fall back to the user's full grant set.
        if (model.RoleTemplateId.HasValue)
        {
            var template = await db.RoleTemplates.AsNoTracking()
                .FirstOrDefaultAsync(t => t.Id == model.RoleTemplateId.Value
                            && t.DeactivatedAt == null, cancellationToken)
                ?? throw new KeyNotFoundException(
                    $"RoleTemplate {model.RoleTemplateId.Value} not found or deactivated.");

            await EnsureTemplateLeavesARoleAsync(model.UserId, template, cancellationToken);
        }

        var keyBytes = RandomNumberGenerator.GetBytes(32);
        var plaintextKey = $"fsk_{Convert.ToBase64String(keyBytes)
            .Replace("+", "").Replace("/", "").Replace("=", "")}";
        var keyPrefix = plaintextKey[..12];

        var hasher = new PasswordHasher<object>();
        var keyHash = hasher.HashPassword(null!, plaintextKey);

        var apiKey = new SystemApiKey
        {
            Name = model.Name,
            KeyHash = keyHash,
            KeyPrefix = keyPrefix,
            UserId = model.UserId,
            RoleTemplateId = model.RoleTemplateId,
            ExpiresAt = model.ExpiresAt,
            ScopesJson = model.Scopes != null
                ? JsonSerializer.Serialize(model.Scopes) : null,
            AllowedIpsJson = model.AllowedIps != null
                ? JsonSerializer.Serialize(model.AllowedIps) : null,
        };

        db.SystemApiKeys.Add(apiKey);
        await db.SaveChangesAsync(cancellationToken);

        // System-wide audit row. NEVER include the plaintext or hash —
        // name + prefix + bound user only.
        var actorId = db.CurrentUserId ?? 0;
        var details = JsonSerializer.Serialize(new
        {
            name = apiKey.Name,
            keyPrefix = apiKey.KeyPrefix,
            userId = apiKey.UserId,
            roleTemplateId = apiKey.RoleTemplateId,
            expiresAt = apiKey.ExpiresAt,
            scopes = model.Scopes,
            allowedIps = model.AllowedIps,
        });
        await auditWriter.WriteAsync(
            action: "SystemApiKeyIssued",
            userId: actorId,
            entityType: nameof(SystemApiKey),
            entityId: apiKey.Id,
            details: details,
            ct: cancellationToken);

        return new CreateSystemApiKeyResponseModel
        {
            Id = apiKey.Id,
            Name = apiKey.Name,
            KeyPrefix = apiKey.KeyPrefix,
            PlaintextKey = plaintextKey,
            UserId = apiKey.UserId,
            ExpiresAt = apiKey.ExpiresAt,
        };
    }

    private async Task EnsureTemplateLeavesARoleAsync(int userId, RoleTemplate template, CancellationToken ct)
    {
        List<string>? templateRoles;
        try
        {
            templateRoles = JsonSerializer.Deserialize<List<string>>(template.IncludedRoleNamesJson);
        }
        catch (JsonException)
        {
            return;
        }
        if (templateRoles is null)
            return;

        var userRoles = await db.UserRoles
            .Where(ur => ur.UserId == userId)
            .Join(db.Roles, ur => ur.RoleId, r => r.Id, (ur, r) => r.Name!)
            .ToListAsync(ct);

        if (!userRoles.Intersect(templateRoles, StringComparer.OrdinalIgnoreCase).Any())
            throw new InvalidOperationException(
                $"RoleTemplate '{template.Name}' shares no role with the bound user, so a key scoped to it could not do anything. "
                + "Pick a template that includes one of the user's roles, or issue the key without a template.");
    }
}
