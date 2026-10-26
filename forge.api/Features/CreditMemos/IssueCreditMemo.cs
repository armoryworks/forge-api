using MediatR;

using Forge.Core.Models;
using Forge.Data.Context;

namespace Forge.Api.Features.CreditMemos;

public record IssueCreditMemoCommand(int Id) : IRequest<CreditMemoDetailResponseModel>;

/// <summary>
/// Issues a draft credit memo (see <see cref="CreditMemoIssuer"/>): posts it and, when it credits an invoice with a
/// balance, applies min(memo, balance) to it, all in one transaction.
/// </summary>
public class IssueCreditMemoHandler(AppDbContext db, CreditMemoIssuer issuer, IMediator mediator)
    : IRequestHandler<IssueCreditMemoCommand, CreditMemoDetailResponseModel>
{
    public async Task<CreditMemoDetailResponseModel> Handle(IssueCreditMemoCommand request, CancellationToken ct)
    {
        await using var tx = db.Database.CurrentTransaction is null ? await db.Database.BeginTransactionAsync(ct) : null;
        var memo = await CreditMemoRules.LoadMemoAsync(db, request.Id, ct)
            ?? throw new KeyNotFoundException($"Credit memo {request.Id} not found");

        await issuer.IssueAsync(memo, ct);

        if (tx is not null)
            await tx.CommitAsync(ct);
        return await mediator.Send(new GetCreditMemoByIdQuery(memo.Id), ct);
    }
}
