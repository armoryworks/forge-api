using System.Security.Claims;

using MediatR;
using Microsoft.AspNetCore.Http;

namespace Forge.Api.Features.Accounting;

/// <summary>Request body for recording actual overhead into the pool.</summary>
public record RecordActualOverheadRequest(decimal Amount, string? Memo, DateOnly EntryDate);

/// <summary>Request body for the period overhead-pool close.</summary>
public record CloseOverheadPoolRequest(DateOnly AsOf);

/// <summary>Record actual overhead incurred into the OVERHEAD_CONTROL pool (dark behind CAP-ACCT-FULLGL).</summary>
public record RecordActualOverheadCommand(decimal Amount, string Memo, DateOnly EntryDate) : IRequest;

public class RecordActualOverheadHandler(
    IOverheadPoolService pool,
    IHttpContextAccessor? httpContextAccessor = null) : IRequestHandler<RecordActualOverheadCommand>
{
    public async Task Handle(RecordActualOverheadCommand request, CancellationToken cancellationToken)
    {
        var userId = int.TryParse(
            httpContextAccessor?.HttpContext?.User.FindFirstValue(ClaimTypes.NameIdentifier),
            out var uid) ? uid : 0;
        await pool.RecordActualOverheadAsync(request.Amount, request.Memo, request.EntryDate, userId, cancellationToken);
    }
}

/// <summary>Close the overhead pool for a period: post the spending variance + clear the pool.</summary>
public record CloseOverheadPoolCommand(DateOnly AsOf) : IRequest<OverheadPoolCloseResult>;

public class CloseOverheadPoolHandler(
    IOverheadPoolService pool,
    IHttpContextAccessor? httpContextAccessor = null) : IRequestHandler<CloseOverheadPoolCommand, OverheadPoolCloseResult>
{
    public async Task<OverheadPoolCloseResult> Handle(CloseOverheadPoolCommand request, CancellationToken cancellationToken)
    {
        var userId = int.TryParse(
            httpContextAccessor?.HttpContext?.User.FindFirstValue(ClaimTypes.NameIdentifier),
            out var uid) ? uid : 0;
        return await pool.CloseOverheadPoolAsync(request.AsOf, userId, cancellationToken);
    }
}
