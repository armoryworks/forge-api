using MediatR;
using Microsoft.EntityFrameworkCore;

using Forge.Core.Enums;
using Forge.Core.Interfaces;
using Forge.Data.Context;

namespace Forge.Api.Features.SalesOrders.Acceptance;

/// <summary>Public read of what the customer is being asked to accept, resolved from an unguessable token.</summary>
public record GetPublicAcceptanceQuery(string Token) : IRequest<PublicAcceptanceViewModel>;

public record PublicAcceptanceLine(string Description, decimal Quantity, decimal UnitPrice);

public record PublicAcceptanceViewModel(
    string OrderNumber, string CustomerName, string CompanyName,
    bool RequiresKey, bool AlreadyResponded, string Status,
    List<PublicAcceptanceLine> Lines, decimal Total);

public class GetPublicAcceptanceHandler(AppDbContext db, ISystemSettingRepository settings, IClock clock)
    : IRequestHandler<GetPublicAcceptanceQuery, PublicAcceptanceViewModel>
{
    public async Task<PublicAcceptanceViewModel> Handle(GetPublicAcceptanceQuery request, CancellationToken cancellationToken)
    {
        var acceptance = await db.SalesOrderAcceptances.AsNoTracking()
            .FirstOrDefaultAsync(a => a.AccessToken == request.Token && a.Method == AcceptanceMethod.PublicPortal, cancellationToken)
            ?? throw new KeyNotFoundException("This acceptance link is not valid.");

        var expired = acceptance.Status == AcceptanceStatus.Pending
            && acceptance.ExpiresAt is not null && acceptance.ExpiresAt < clock.UtcNow;
        var status = expired ? AcceptanceStatus.Expired.ToString() : acceptance.Status.ToString();
        var alreadyResponded = acceptance.Status != AcceptanceStatus.Pending || expired;

        var order = await db.SalesOrders.AsNoTracking()
            .Include(o => o.Customer)
            .Include(o => o.Lines)
            .FirstAsync(o => o.Id == acceptance.SalesOrderId, cancellationToken);

        var companyName = (await settings.FindByKeyAsync("company.name", cancellationToken))?.Value ?? "Forge";
        var lines = order.Lines
            .Select(l => new PublicAcceptanceLine(l.Description, l.Quantity, l.UnitPrice))
            .ToList();
        var total = order.Lines.Sum(l => l.Quantity * l.UnitPrice);

        return new PublicAcceptanceViewModel(
            order.OrderNumber, order.Customer?.Name ?? "Customer", companyName,
            acceptance.VerificationKeyHash is not null, alreadyResponded, status, lines, total);
    }
}
