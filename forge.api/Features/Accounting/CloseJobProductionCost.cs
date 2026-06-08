using System.Security.Claims;

using MediatR;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;

using Forge.Core.Interfaces;
using Forge.Data.Context;

namespace Forge.Api.Features.Accounting;

/// <summary>
/// Close a job's production cost: absorb its actual labor + overhead into WIP and sweep the remaining WIP
/// balance to PRODUCTION_VARIANCE (dark behind CAP-ACCT-FULLGL; idempotent). Run after all of the job's
/// production receipts.
/// </summary>
public record CloseJobProductionCostCommand(int JobId) : IRequest<JobProductionCostCloseResult>;

public class CloseJobProductionCostHandler(
    AppDbContext db,
    IProductionVariancePostingService posting,
    IClock clock,
    IHttpContextAccessor? httpContextAccessor = null)
    : IRequestHandler<CloseJobProductionCostCommand, JobProductionCostCloseResult>
{
    public async Task<JobProductionCostCloseResult> Handle(
        CloseJobProductionCostCommand request, CancellationToken cancellationToken)
    {
        var exists = await db.Jobs.AnyAsync(j => j.Id == request.JobId, cancellationToken);
        if (!exists)
            throw new KeyNotFoundException($"Job {request.JobId} not found.");

        // Server-trusted posting principal (PostedBy) from the auth context, mirroring the other GL handlers.
        var userId = int.TryParse(
            httpContextAccessor?.HttpContext?.User.FindFirstValue(ClaimTypes.NameIdentifier),
            out var uid) ? uid : 0;

        var entryDate = DateOnly.FromDateTime(clock.UtcNow.UtcDateTime);

        // One transaction for the absorption + variance entries (Npgsql; no-op on the in-memory provider).
        await using var tx = await db.Database.BeginTransactionAsync(cancellationToken);

        var result = await posting.CloseJobProductionCostAsync(
            request.JobId, entryDate, userId, cancellationToken);

        await tx.CommitAsync(cancellationToken);
        return result;
    }
}
