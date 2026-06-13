using System.Security.Claims;

using MediatR;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;

using Forge.Api.Capabilities;
using Forge.Core.Interfaces;
using Forge.Core.Models.Accounting;
using Forge.Data.Context;

namespace Forge.Api.Features.Accounting;

/// <summary>Phase-4 — register a fixed asset. CAP-ACCT-DEPRECIATION gated.</summary>
[RequiresCapability("CAP-ACCT-DEPRECIATION")]
public record CreateFixedAssetCommand(CreateFixedAssetModel Model) : IRequest<FixedAssetModel>;

public class CreateFixedAssetHandler(IDepreciationService service)
    : IRequestHandler<CreateFixedAssetCommand, FixedAssetModel>
{
    public Task<FixedAssetModel> Handle(CreateFixedAssetCommand request, CancellationToken ct)
        => service.CreateAssetAsync(request.Model, ct);
}

/// <summary>Phase-4 — list a book's fixed assets. CAP-ACCT-DEPRECIATION gated.</summary>
[RequiresCapability("CAP-ACCT-DEPRECIATION")]
public record ListFixedAssetsQuery(int BookId) : IRequest<IReadOnlyList<FixedAssetModel>>;

public class ListFixedAssetsHandler(IDepreciationService service)
    : IRequestHandler<ListFixedAssetsQuery, IReadOnlyList<FixedAssetModel>>
{
    public Task<IReadOnlyList<FixedAssetModel>> Handle(ListFixedAssetsQuery request, CancellationToken ct)
        => service.ListAssetsAsync(request.BookId, ct);
}

/// <summary>Phase-4 — run a month's depreciation for a book. CAP-ACCT-DEPRECIATION gated.</summary>
[RequiresCapability("CAP-ACCT-DEPRECIATION")]
public record RunDepreciationCommand(int BookId, DateOnly PeriodMonth) : IRequest<DepreciationRunResult>;

public class RunDepreciationHandler(
    IDepreciationService service,
    IHttpContextAccessor? httpContextAccessor = null,
    AppDbContext? db = null)
    : IRequestHandler<RunDepreciationCommand, DepreciationRunResult>
{
    public async Task<DepreciationRunResult> Handle(RunDepreciationCommand request, CancellationToken ct)
    {
        var userId = int.TryParse(
            httpContextAccessor?.HttpContext?.User.FindFirstValue(ClaimTypes.NameIdentifier),
            out var uid) ? uid : 0;

        await using var tx = db is not null ? await db.Database.BeginTransactionAsync(ct) : null;
        var result = await service.RunDepreciationAsync(request.BookId, request.PeriodMonth, userId, ct);
        if (tx is not null) await tx.CommitAsync(ct);
        return result;
    }
}
