using System.Security.Claims;

using MediatR;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;

using Forge.Api.Capabilities;
using Forge.Core.Interfaces;
using Forge.Core.Models.Accounting;
using Forge.Data.Context;

namespace Forge.Api.Features.Accounting;

/// <summary>
/// Phase-3 — year-end close / Retained-Earnings roll-forward. Posts the closing entry (P&amp;L → RE), hard-
/// closes every period, marks the year Closed — all in one transaction.
/// <para><b>Gated</b> on <c>CAP-ACCT-FULLGL</c>; OFF by default (unreachable), Controller-role per §5.7.</para>
/// </summary>
[RequiresCapability("CAP-ACCT-FULLGL")]
public record CloseFiscalYearCommand(int FiscalYearId) : IRequest<YearEndCloseResult>;

public class CloseFiscalYearHandler(
    IYearEndCloseService closeService,
    IHttpContextAccessor? httpContextAccessor = null,
    AppDbContext? db = null)
    : IRequestHandler<CloseFiscalYearCommand, YearEndCloseResult>
{
    public async Task<YearEndCloseResult> Handle(CloseFiscalYearCommand request, CancellationToken cancellationToken)
    {
        var userId = int.TryParse(
            httpContextAccessor?.HttpContext?.User.FindFirstValue(ClaimTypes.NameIdentifier),
            out var uid) ? uid : 0;

        // One transaction so the closing entry + period/year lock commit (or roll back) together.
        await using var tx = db is not null
            ? await db.Database.BeginTransactionAsync(cancellationToken)
            : null;

        var result = await closeService.CloseYearAsync(request.FiscalYearId, userId, cancellationToken);

        if (tx is not null)
            await tx.CommitAsync(cancellationToken);

        return result;
    }
}
