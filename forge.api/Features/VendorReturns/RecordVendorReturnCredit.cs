using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;

using Forge.Api.Capabilities;
using Forge.Api.Features.VendorCredits;
using Forge.Core.Enums;
using Forge.Core.Models;
using Forge.Data.Context;

namespace Forge.Api.Features.VendorReturns;

[RequiresCapability("CAP-P2P-BILL")]
public record RecordVendorReturnCreditCommand(int Id, RecordVendorReturnCreditRequestModel Request) : IRequest<VendorCreditMemoDetailResponseModel>;

public class RecordVendorReturnCreditValidator : AbstractValidator<RecordVendorReturnCreditCommand>
{
    public RecordVendorReturnCreditValidator()
    {
        RuleFor(x => x.Request.VendorReference).MaximumLength(100);
        RuleFor(x => x.Request.Notes).MaximumLength(2000);
        RuleForEach(x => x.Request.Lines).ChildRules(line =>
        {
            line.RuleFor(l => l.Quantity).GreaterThan(0m).When(l => l.Quantity.HasValue);
            line.RuleFor(l => l.UnitPrice).GreaterThanOrEqualTo(0m).When(l => l.UnitPrice.HasValue);
        });
    }
}

/// <summary>
/// Records the vendor's credit for a shipped return: creates a vendor credit memo (source Return) for the return's
/// lines, by default every line at its full quantity and purchase price, and issues it in the same transaction. The
/// memo clears the vendor returns clearing account and the return moves to Credited (<c>vendor-return-not-shipped</c>
/// while it has not shipped).
/// </summary>
public class RecordVendorReturnCreditHandler(AppDbContext db, ISender sender)
    : IRequestHandler<RecordVendorReturnCreditCommand, VendorCreditMemoDetailResponseModel>
{
    public const string NotShippedCode = "vendor-return-not-shipped";

    public async Task<VendorCreditMemoDetailResponseModel> Handle(RecordVendorReturnCreditCommand command, CancellationToken ct)
    {
        var rtv = await db.VendorReturns.AsNoTracking()
            .Include(r => r.Lines)
            .Include(r => r.NonConformance)
            .FirstOrDefaultAsync(r => r.Id == command.Id, ct)
            ?? throw new KeyNotFoundException($"Vendor return {command.Id} not found");
        if (rtv.Status != VendorReturnStatus.Shipped)
            throw VendorCreditMemoRules.Failure("Id", NotShippedCode,
                $"Return {rtv.ReturnNumber} is {rtv.Status}; record the vendor's credit once it has shipped.");

        var request = command.Request;
        var picked = request.Lines is { Count: > 0 }
            ? request.Lines
            : rtv.Lines.Select(l => new VendorReturnCreditLineRequestModel(l.Id)).ToList();
        var lines = new List<VendorCreditMemoLineRequestModel>(picked.Count);
        foreach (var pick in picked)
        {
            var line = rtv.Lines.FirstOrDefault(l => l.Id == pick.VendorReturnLineId)
                ?? throw VendorCreditMemoRules.Failure("Lines", VendorCreditMemoRules.ReturnLineCode,
                    "A credited return line must belong to the return to vendor.");
            lines.Add(new VendorCreditMemoLineRequestModel(
                pick.Quantity ?? line.Quantity,
                pick.UnitPrice ?? line.UnitPrice,
                VendorReturnLineId: line.Id,
                PartId: line.PartId,
                Description: string.IsNullOrWhiteSpace(line.LotNumber)
                    ? $"Returned on {rtv.ReturnNumber}"
                    : $"Returned on {rtv.ReturnNumber}, lot {line.LotNumber}"));
        }

        await using var tx = db.Database.CurrentTransaction is null ? await db.Database.BeginTransactionAsync(ct) : null;
        var memo = await sender.Send(new CreateVendorCreditMemoCommand(
            new CreateVendorCreditMemoRequestModel(
                rtv.VendorId,
                VendorCreditMemoSourceType.Return,
                rtv.NonConformance is null ? $"Return to vendor {rtv.ReturnNumber}" : $"Return to vendor {rtv.ReturnNumber} ({rtv.NonConformance.NcrNumber})",
                lines,
                VendorReference: request.VendorReference,
                MemoDate: request.MemoDate,
                Notes: request.Notes),
            rtv.Id), ct);
        var issued = await sender.Send(new IssueVendorCreditMemoCommand(memo.Id), ct);
        if (tx is not null)
            await tx.CommitAsync(ct);
        return issued;
    }
}
