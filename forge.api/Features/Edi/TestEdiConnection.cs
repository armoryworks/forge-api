using MediatR;
using Microsoft.EntityFrameworkCore;

using Forge.Core.Interfaces;
using Forge.Data.Context;

namespace Forge.Api.Features.Edi;

public record TestEdiConnectionCommand(int TradingPartnerId) : IRequest<TestEdiConnectionResult>;

public record TestEdiConnectionResult(bool Success, string Message);

public class TestEdiConnectionHandler(AppDbContext db, IEdiTransportService transportService)
    : IRequestHandler<TestEdiConnectionCommand, TestEdiConnectionResult>
{
    public async Task<TestEdiConnectionResult> Handle(
        TestEdiConnectionCommand request, CancellationToken cancellationToken)
    {
        var partner = await db.EdiTradingPartners
            .AsNoTracking()
            .FirstOrDefaultAsync(p => p.Id == request.TradingPartnerId, cancellationToken)
            ?? throw new KeyNotFoundException($"Trading partner {request.TradingPartnerId} not found");

        var success = await transportService.TestConnectionAsync(
            partner.TransportConfigJson ?? "{}", cancellationToken);

        return new TestEdiConnectionResult(
            success,
            success ? "Connection successful" : "Connection failed");
    }
}
