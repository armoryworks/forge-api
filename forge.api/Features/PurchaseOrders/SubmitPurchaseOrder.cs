using System.Security.Claims;

using MediatR;
using Microsoft.AspNetCore.Http;

using Forge.Core.Interfaces;

namespace Forge.Api.Features.PurchaseOrders;

public record SubmitPurchaseOrderCommand(int Id) : IRequest;

public class SubmitPurchaseOrderHandler(
    IPurchaseOrderRepository repo,
    IApprovalService approvalService,
    ICurrencyService currencyService,
    IHttpContextAccessor httpContext)
    : IRequestHandler<SubmitPurchaseOrderCommand>
{
    public async Task Handle(SubmitPurchaseOrderCommand request, CancellationToken cancellationToken)
    {
        var po = await repo.FindWithDetailsAsync(request.Id, cancellationToken)
            ?? throw new KeyNotFoundException($"Purchase order {request.Id} not found");

        if (po.Status != PurchaseOrderStatus.Draft)
            throw new InvalidOperationException("Only Draft purchase orders can be submitted");

        // Bought-parts effort PR2.5 — FX rate snapshot lock at Submit. Until
        // submit, the QuoteCurrency total is informational; once locked, the
        // landed-cost calc and cost tab use this rate even if subsequent
        // market rates move. Same-currency POs auto-lock at 1.0 with the
        // reason recorded; mismatched currency requires the user to have
        // entered a rate before submitting (we don't auto-fetch live FX in
        // v1 — the user is the source of truth).
        if (!po.FxRate.HasValue)
        {
            var baseCurrency = await currencyService.GetBaseCurrencyAsync(cancellationToken);
            if (string.Equals(po.QuoteCurrency, baseCurrency, StringComparison.OrdinalIgnoreCase))
            {
                po.FxRate = 1.0m;
                if (string.IsNullOrEmpty(po.FxRateSource))
                    po.FxRateSource = $"auto: same-currency ({baseCurrency})";
            }
            else
            {
                throw new InvalidOperationException(
                    $"FX rate is required before submitting a {po.QuoteCurrency} PO (base currency is {baseCurrency}).");
            }
        }

        po.Status = PurchaseOrderStatus.Submitted;
        po.SubmittedDate = DateTimeOffset.UtcNow;

        await repo.SaveChangesAsync(cancellationToken);

        // Submit for approval if a workflow matches. Approval is advisory here —
        // it does not gate the PO submission itself. This populates the generic
        // /approvals inbox so Managers can review high-value POs.
        var claim = httpContext.HttpContext?.User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (int.TryParse(claim, out var userId) && userId > 0)
        {
            var totalAmount = po.Lines.Sum(l => l.OrderedQuantity * l.UnitPrice);
            var summary = $"PO {po.PONumber} — {po.Lines.Count} line(s)";
            await approvalService.SubmitForApprovalAsync(
                "PurchaseOrder", po.Id, userId, totalAmount, summary, cancellationToken);
        }
    }
}
