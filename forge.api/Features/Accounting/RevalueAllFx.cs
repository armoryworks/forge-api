using System.Security.Claims;

using MediatR;
using Microsoft.AspNetCore.Http;

using Forge.Api.Capabilities;
using Forge.Core.Interfaces;
using Forge.Core.Models.Accounting;
using Forge.Data.Context;

namespace Forge.Api.Features.Accounting;

/// <summary>
/// Revalue every active foreign currency with a monetary position at the rate on file for <c>AsOf</c>, in one
/// transaction. <c>Preview</c> computes the adjustments without posting. CAP-ACCT-FXREVAL gated.
/// </summary>
[RequiresCapability("CAP-ACCT-FXREVAL")]
public record RevalueAllFxCommand(int BookId, DateOnly AsOf, bool Preview = false)
    : IRequest<IReadOnlyList<FxRevaluationResult>>;

public class RevalueAllFxHandler(
    IFxRevaluationService service,
    IHttpContextAccessor? httpContextAccessor = null,
    AppDbContext? db = null)
    : IRequestHandler<RevalueAllFxCommand, IReadOnlyList<FxRevaluationResult>>
{
    public async Task<IReadOnlyList<FxRevaluationResult>> Handle(RevalueAllFxCommand request, CancellationToken ct)
    {
        var userId = int.TryParse(
            httpContextAccessor?.HttpContext?.User.FindFirstValue(ClaimTypes.NameIdentifier),
            out var uid) ? uid : 0;

        await using var tx = db is not null && !request.Preview ? await db.Database.BeginTransactionAsync(ct) : null;
        var results = await service.RevalueAllAsync(request.BookId, request.AsOf, userId, ct, request.Preview);
        if (tx is not null) await tx.CommitAsync(ct);
        return results;
    }
}
