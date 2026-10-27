using MediatR;
using Microsoft.EntityFrameworkCore;

using Forge.Core.Entities;
using Forge.Core.Enums;
using Forge.Core.Interfaces;
using Forge.Core.Models;
using Forge.Data.Context;

namespace Forge.Api.Features.CloudStorage;

public record TestCloudStorageProviderCommand(int Id, int UserId) : IRequest<CloudStorageTestResultModel>;

public class TestCloudStorageProviderHandler(
    AppDbContext db,
    ICloudStorageResolver resolver,
    ICloudStorageTokenManager tokens,
    IClock clock) : IRequestHandler<TestCloudStorageProviderCommand, CloudStorageTestResultModel>
{
    public async Task<CloudStorageTestResultModel> Handle(TestCloudStorageProviderCommand request, CancellationToken ct)
    {
        var provider = await db.CloudStorageProviders.FirstOrDefaultAsync(p => p.Id == request.Id, ct)
            ?? throw new KeyNotFoundException($"Cloud storage provider {request.Id} not found.");

        var service = resolver.ResolveByCode(provider.ProviderCode);
        if (service is null)
            return await FailAsync(provider, $"The {provider.ProviderCode} connector is not configured on this server.", ct);

        string? token;
        if (provider.Mode == CloudStorageProviderMode.PerUser && provider.ProviderCode != CloudStorageProviderCodes.Mock)
        {
            var link = await db.UserCloudStorageLinks
                .FirstOrDefaultAsync(l => l.ProviderId == provider.Id && l.UserId == request.UserId, ct);
            if (link is null)
                return await FailAsync(provider, "Per-user mode: connect your own account to this provider, then test again.", ct);
            token = await tokens.GetValidAccessTokenAsync(link, ct);
        }
        else
        {
            token = await tokens.GetValidAccessTokenAsync(provider, ct);
        }

        if (string.IsNullOrEmpty(token))
            return await FailAsync(provider, "No usable access token. Enter the provider credentials again.", ct);

        CloudStorageHealthResult health;
        try
        {
            health = await service.HealthCheckAsync(token, ct);
        }
        catch (HttpRequestException ex)
        {
            return await FailAsync(provider, ex.Message, ct);
        }

        if (!health.IsHealthy)
            return await FailAsync(provider, health.Message, ct, health.LatencyMs);

        provider.LastConnectedAt = clock.UtcNow;
        provider.LastError = null;
        await db.SaveChangesAsync(ct);
        return new CloudStorageTestResultModel(true, health.Message, health.LatencyMs);
    }

    private async Task<CloudStorageTestResultModel> FailAsync(
        CloudStorageProvider provider, string message, CancellationToken ct, long latencyMs = 0)
    {
        provider.LastError = message.Length > 2000 ? message[..2000] : message;
        await db.SaveChangesAsync(ct);
        return new CloudStorageTestResultModel(false, message, latencyMs);
    }
}
