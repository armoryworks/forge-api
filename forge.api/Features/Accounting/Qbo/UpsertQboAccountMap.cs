using MediatR;
using Microsoft.EntityFrameworkCore;

using Forge.Api.Capabilities;
using Forge.Core.Entities.Accounting;
using Forge.Core.Models.Accounting;
using Forge.Data.Context;

namespace Forge.Api.Features.Accounting.Qbo;

/// <summary>
/// QB-001 — upsert the QBO mapping for one GL account (unique per account).
/// No per-entity activity log per the GL-subsystem precedent (system-level
/// configuration, not a tracked business entity).
/// </summary>
[RequiresCapability("CAP-ACCT-QBO-EXPORT")]
public record UpsertQboAccountMapCommand(
    int GlAccountId,
    string QboAccountId,
    string? QboAccountName) : IRequest<QboAccountMappingModel>;

public class UpsertQboAccountMapHandler(AppDbContext db)
    : IRequestHandler<UpsertQboAccountMapCommand, QboAccountMappingModel>
{
    public async Task<QboAccountMappingModel> Handle(
        UpsertQboAccountMapCommand request, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.QboAccountId))
            throw new InvalidOperationException("A QuickBooks account id is required.");

        var account = await db.GlAccounts.AsNoTracking()
            .FirstOrDefaultAsync(a => a.Id == request.GlAccountId, cancellationToken)
            ?? throw new KeyNotFoundException($"GL account {request.GlAccountId} not found.");

        var map = await db.QboAccountMaps
            .FirstOrDefaultAsync(m => m.GlAccountId == request.GlAccountId, cancellationToken);

        if (map is null)
        {
            map = new QboAccountMap { GlAccountId = request.GlAccountId };
            db.QboAccountMaps.Add(map);
        }

        map.QboAccountId = request.QboAccountId.Trim();
        map.QboAccountName = request.QboAccountName;

        await db.SaveChangesAsync(cancellationToken);

        return new QboAccountMappingModel(
            account.Id, account.AccountNumber, account.Name, map.QboAccountId, map.QboAccountName);
    }
}
