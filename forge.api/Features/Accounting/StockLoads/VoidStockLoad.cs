using System.Security.Claims;

using MediatR;
using Microsoft.AspNetCore.Http;

using Forge.Api.Capabilities;

namespace Forge.Api.Features.Accounting.StockLoads;

/// <summary>Voids a posted stock load while every loaded quantity is still on hand at its location: bins, lots and the store come back out and the journal is countered.</summary>
[RequiresAnyCapability("CAP-ACCT-MIGRATION", "CAP-ACCT-FULLGL")]
public record VoidStockLoadCommand(int Id) : IRequest<StockLoadModel>;

public class VoidStockLoadHandler(IStockLoadService stockLoads, IHttpContextAccessor httpContext)
    : IRequestHandler<VoidStockLoadCommand, StockLoadModel>
{
    public async Task<StockLoadModel> Handle(VoidStockLoadCommand request, CancellationToken ct)
    {
        var userId = int.Parse(httpContext.HttpContext?.User.FindFirstValue(ClaimTypes.NameIdentifier) ?? "0");
        await stockLoads.VoidAsync(request.Id, userId, ct);
        return await stockLoads.GetModelAsync(request.Id, ct);
    }
}
