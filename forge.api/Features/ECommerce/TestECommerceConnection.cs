using MediatR;
using Microsoft.EntityFrameworkCore;

using Forge.Core.Interfaces;
using Forge.Data.Context;

namespace Forge.Api.Features.ECommerce;

public record TestECommerceConnectionCommand(int Id) : IRequest<TestECommerceConnectionResult>;

public record TestECommerceConnectionResult(bool Success, string? ErrorMessage);

public class TestECommerceConnectionHandler(AppDbContext db, IECommerceService eCommerceService)
    : IRequestHandler<TestECommerceConnectionCommand, TestECommerceConnectionResult>
{
    public async Task<TestECommerceConnectionResult> Handle(
        TestECommerceConnectionCommand request, CancellationToken cancellationToken)
    {
        var integration = await db.ECommerceIntegrations
            .AsNoTracking()
            .FirstOrDefaultAsync(i => i.Id == request.Id, cancellationToken)
            ?? throw new KeyNotFoundException($"ECommerceIntegration {request.Id} not found");

        try
        {
            var success = await eCommerceService.TestConnectionAsync(
                integration.EncryptedCredentials, integration.StoreUrl ?? string.Empty, cancellationToken);
            return new TestECommerceConnectionResult(success, success ? null : "Connection test failed");
        }
        catch (Exception ex)
        {
            return new TestECommerceConnectionResult(false, ex.Message);
        }
    }
}
