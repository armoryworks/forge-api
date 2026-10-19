using System.Security.Claims;

using MediatR;
using Microsoft.AspNetCore.Http;

using Forge.Api.Capabilities;
using Forge.Core.Interfaces;
using Forge.Core.Models.Accounting;
using Forge.Data.Context;

namespace Forge.Api.Features.Accounting;

/// <summary>Dispose of a fixed asset and post its gain/loss. CAP-ACCT-DEPRECIATION gated.</summary>
[RequiresCapability("CAP-ACCT-DEPRECIATION")]
public record DisposeFixedAssetCommand(int FixedAssetId, DisposeFixedAssetModel Model) : IRequest<FixedAssetDisposalResult>;

public class DisposeFixedAssetHandler(
    IDepreciationService service,
    IHttpContextAccessor? httpContextAccessor = null,
    AppDbContext? db = null)
    : IRequestHandler<DisposeFixedAssetCommand, FixedAssetDisposalResult>
{
    public async Task<FixedAssetDisposalResult> Handle(DisposeFixedAssetCommand request, CancellationToken ct)
    {
        var userId = int.TryParse(
            httpContextAccessor?.HttpContext?.User.FindFirstValue(ClaimTypes.NameIdentifier),
            out var uid) ? uid : 0;

        await using var tx = db is not null ? await db.Database.BeginTransactionAsync(ct) : null;
        var result = await service.DisposeAssetAsync(request.FixedAssetId, request.Model, userId, ct);
        if (tx is not null) await tx.CommitAsync(ct);
        return result;
    }
}
