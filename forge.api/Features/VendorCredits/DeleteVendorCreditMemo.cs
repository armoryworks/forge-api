using MediatR;

using Forge.Core.Enums;
using Forge.Core.Interfaces;
using Forge.Data.Context;
using Forge.Data.Extensions;

namespace Forge.Api.Features.VendorCredits;

public record DeleteVendorCreditMemoCommand(int Id) : IRequest;

/// <summary>Soft-deletes a draft vendor memo. Issued memos are voided instead.</summary>
public class DeleteVendorCreditMemoHandler(AppDbContext db, IClock clock) : IRequestHandler<DeleteVendorCreditMemoCommand>
{
    public async Task Handle(DeleteVendorCreditMemoCommand request, CancellationToken ct)
    {
        var memo = await db.VendorCreditMemos.FindAsync([request.Id], ct)
            ?? throw new KeyNotFoundException($"Vendor credit memo {request.Id} not found");
        if (memo.Status != VendorCreditMemoStatus.Draft)
            throw VendorCreditMemoRules.Failure("Id", VendorCreditMemoRules.NotDraftCode,
                $"Vendor memo {memo.MemoNumber} is {memo.Status}; only a draft can be deleted. Void it instead.");

        memo.DeletedAt = clock.UtcNow;
        db.LogActivityAt("deleted", $"Deleted draft vendor memo {memo.MemoNumber}", ("VendorCreditMemo", memo.Id));
        await db.SaveChangesAsync(ct);
    }
}
