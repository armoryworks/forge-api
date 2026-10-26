using MediatR;

using Forge.Core.Enums;
using Forge.Core.Interfaces;
using Forge.Data.Context;
using Forge.Data.Extensions;

namespace Forge.Api.Features.CreditMemos;

public record DeleteCreditMemoCommand(int Id) : IRequest;

/// <summary>Soft-deletes a draft credit memo. Issued memos are voided instead.</summary>
public class DeleteCreditMemoHandler(AppDbContext db, IClock clock) : IRequestHandler<DeleteCreditMemoCommand>
{
    public async Task Handle(DeleteCreditMemoCommand request, CancellationToken ct)
    {
        var memo = await db.CreditMemos.FindAsync([request.Id], ct)
            ?? throw new KeyNotFoundException($"Credit memo {request.Id} not found");
        if (memo.Status != CreditMemoStatus.Draft)
            throw CreditMemoRules.Failure("Id", CreditMemoRules.CreditMemoNotDraftCode,
                $"Credit memo {memo.CreditMemoNumber} is {memo.Status}; only a draft can be deleted. Void it instead.");

        memo.DeletedAt = clock.UtcNow;
        db.LogActivityAt("deleted", $"Deleted draft credit memo {memo.CreditMemoNumber}", ("CreditMemo", memo.Id));
        await db.SaveChangesAsync(ct);
    }
}
