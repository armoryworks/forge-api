using MediatR;

using Forge.Api.Capabilities;
using Forge.Core.Interfaces;
using Forge.Core.Models.Accounting;
using Forge.Data.Context;

namespace Forge.Api.Features.Accounting;

/// <summary>One fixed asset with its posted and projected depreciation schedule. CAP-ACCT-DEPRECIATION gated.</summary>
[RequiresCapability("CAP-ACCT-DEPRECIATION")]
public record GetFixedAssetQuery(int FixedAssetId) : IRequest<FixedAssetDetailModel>;

public class GetFixedAssetHandler(IDepreciationService service)
    : IRequestHandler<GetFixedAssetQuery, FixedAssetDetailModel>
{
    public Task<FixedAssetDetailModel> Handle(GetFixedAssetQuery request, CancellationToken ct)
        => service.GetAssetAsync(request.FixedAssetId, ct);
}

/// <summary>Edit a fixed asset until its first depreciation posts. CAP-ACCT-DEPRECIATION gated.</summary>
[RequiresCapability("CAP-ACCT-DEPRECIATION")]
public record UpdateFixedAssetCommand(int FixedAssetId, UpdateFixedAssetModel Model) : IRequest<FixedAssetDetailModel>;

public class UpdateFixedAssetHandler(IDepreciationService service, AppDbContext? db = null)
    : IRequestHandler<UpdateFixedAssetCommand, FixedAssetDetailModel>
{
    public async Task<FixedAssetDetailModel> Handle(UpdateFixedAssetCommand request, CancellationToken ct)
    {
        await using var tx = db is not null ? await db.Database.BeginTransactionAsync(ct) : null;
        var result = await service.UpdateAssetAsync(request.FixedAssetId, request.Model, ct);
        if (tx is not null) await tx.CommitAsync(ct);
        return result;
    }
}
