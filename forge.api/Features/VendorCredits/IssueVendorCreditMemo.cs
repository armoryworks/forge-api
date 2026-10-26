using MediatR;

using Forge.Core.Models;
using Forge.Data.Context;

namespace Forge.Api.Features.VendorCredits;

public record IssueVendorCreditMemoCommand(int Id) : IRequest<VendorCreditMemoDetailResponseModel>;

/// <summary>
/// Issues a draft vendor memo (see <see cref="VendorCreditMemoIssuer"/>): posts it and, when it credits a bill with a
/// balance, applies min(memo, balance) to it, all in one transaction.
/// </summary>
public class IssueVendorCreditMemoHandler(AppDbContext db, VendorCreditMemoIssuer issuer, IMediator mediator)
    : IRequestHandler<IssueVendorCreditMemoCommand, VendorCreditMemoDetailResponseModel>
{
    public async Task<VendorCreditMemoDetailResponseModel> Handle(IssueVendorCreditMemoCommand request, CancellationToken ct)
    {
        await using var tx = db.Database.CurrentTransaction is null ? await db.Database.BeginTransactionAsync(ct) : null;
        var memo = await VendorCreditMemoRules.LoadMemoAsync(db, request.Id, ct)
            ?? throw new KeyNotFoundException($"Vendor credit memo {request.Id} not found");

        await issuer.IssueAsync(memo, ct);

        if (tx is not null)
            await tx.CommitAsync(ct);
        return await mediator.Send(new GetVendorCreditMemoByIdQuery(memo.Id), ct);
    }
}
