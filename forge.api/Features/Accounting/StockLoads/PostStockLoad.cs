using System.Security.Claims;

using MediatR;
using Microsoft.AspNetCore.Http;

using Forge.Api.Capabilities;

namespace Forge.Api.Features.Accounting.StockLoads;

/// <summary>Posts a draft stock load: bins and lots, the valuation store, and (OffsetEquity) Dr inventory / Cr Opening Balance Equity.</summary>
[RequiresAnyCapability("CAP-ACCT-MIGRATION", "CAP-ACCT-FULLGL")]
public record PostStockLoadCommand(int Id) : IRequest<StockLoadModel>;

public class PostStockLoadHandler(IStockLoadService stockLoads, IHttpContextAccessor httpContext)
    : IRequestHandler<PostStockLoadCommand, StockLoadModel>
{
    public async Task<StockLoadModel> Handle(PostStockLoadCommand request, CancellationToken ct)
    {
        var userId = int.Parse(httpContext.HttpContext?.User.FindFirstValue(ClaimTypes.NameIdentifier) ?? "0");
        await stockLoads.PostAsync(request.Id, userId, ct);
        return await stockLoads.GetModelAsync(request.Id, ct);
    }
}
