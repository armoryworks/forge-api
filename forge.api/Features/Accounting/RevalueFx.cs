using System.Security.Claims;

using MediatR;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;

using Forge.Api.Capabilities;
using Forge.Core.Interfaces;
using Forge.Core.Models.Accounting;
using Forge.Data.Context;

namespace Forge.Api.Features.Accounting;

/// <summary>Phase-4b — run a period-end unrealized FX revaluation for a currency. CAP-ACCT-FXREVAL gated.</summary>
[RequiresCapability("CAP-ACCT-FXREVAL")]
public record RevalueFxCommand(int BookId, int CurrencyId, decimal NewRate, DateOnly AsOf)
    : IRequest<FxRevaluationResult>;

public class RevalueFxHandler(
    IFxRevaluationService service,
    IHttpContextAccessor? httpContextAccessor = null,
    AppDbContext? db = null)
    : IRequestHandler<RevalueFxCommand, FxRevaluationResult>
{
    public async Task<FxRevaluationResult> Handle(RevalueFxCommand request, CancellationToken ct)
    {
        var userId = int.TryParse(
            httpContextAccessor?.HttpContext?.User.FindFirstValue(ClaimTypes.NameIdentifier),
            out var uid) ? uid : 0;

        await using var tx = db is not null ? await db.Database.BeginTransactionAsync(ct) : null;
        var result = await service.RevalueAsync(request.BookId, request.CurrencyId, request.NewRate, request.AsOf, userId, ct);
        if (tx is not null) await tx.CommitAsync(ct);
        return result;
    }
}
