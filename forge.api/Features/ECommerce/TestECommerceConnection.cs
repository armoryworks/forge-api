using MediatR;
using Microsoft.EntityFrameworkCore;

using Forge.Api.Services;
using Forge.Core.Interfaces;
using Forge.Data.Context;

namespace Forge.Api.Features.ECommerce;

public record TestECommerceConnectionCommand(int Id) : IRequest<TestECommerceConnectionResult>;

public record TestECommerceConnectionResult(bool Success, string? ErrorMessage);

public class TestECommerceConnectionHandler(
    AppDbContext db, IECommerceServiceFactory connectorFactory, IECommerceCredentialProtector protector)
    : IRequestHandler<TestECommerceConnectionCommand, TestECommerceConnectionResult>
{
    public async Task<TestECommerceConnectionResult> Handle(
        TestECommerceConnectionCommand request, CancellationToken ct)
    {
        var integration = await db.ECommerceIntegrations
            .AsNoTracking()
            .FirstOrDefaultAsync(i => i.Id == request.Id, ct)
            ?? throw new KeyNotFoundException($"ECommerceIntegration {request.Id} not found");

        // Resolve the connector for THIS integration's platform. Previously this
        // injected IECommerceService directly, which — now that several
        // connectors are registered — would have handed back whichever one the
        // container resolved last and cheerfully reported a green connection
        // test against an entirely different platform.
        if (!connectorFactory.IsSupported(integration.Platform))
        {
            return new TestECommerceConnectionResult(
                false,
                $"No connector is available for {integration.Platform} yet, so this integration cannot be tested " +
                "or polled. Orders for it have to be entered through the retail-order endpoint.");
        }

        var connector = connectorFactory.For(integration.Platform);

        try
        {
            var success = await connector.TestConnectionAsync(
                protector.Unprotect(integration.EncryptedCredentials) ?? string.Empty, integration.StoreUrl ?? string.Empty, ct);
            return new TestECommerceConnectionResult(success, success ? null : "Connection test failed");
        }
        catch (Exception ex)
        {
            return new TestECommerceConnectionResult(false, ex.Message);
        }
    }
}
