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
/// §7A conversion — post the opening-balance journal at go-live. CAP-ACCT-FULLGL gated; one transaction.
/// </summary>
[RequiresCapability("CAP-ACCT-FULLGL")]
public record PostOpeningBalancesCommand(PostOpeningBalancesModel Model) : IRequest<OpeningBalanceResult>;

public class PostOpeningBalancesHandler(
    IConversionService service,
    IHttpContextAccessor? httpContextAccessor = null,
    AppDbContext? db = null)
    : IRequestHandler<PostOpeningBalancesCommand, OpeningBalanceResult>
{
    public async Task<OpeningBalanceResult> Handle(PostOpeningBalancesCommand request, CancellationToken ct)
    {
        var userId = int.TryParse(
            httpContextAccessor?.HttpContext?.User.FindFirstValue(ClaimTypes.NameIdentifier),
            out var uid) ? uid : 0;

        await using var tx = db is not null ? await db.Database.BeginTransactionAsync(ct) : null;
        var result = await service.PostOpeningBalancesAsync(request.Model, userId, ct);
        if (tx is not null) await tx.CommitAsync(ct);
        return result;
    }
}
