using MediatR;
using Microsoft.EntityFrameworkCore;
using Forge.Core.Models;
using Forge.Data.Context;

namespace Forge.Api.Features.SalesOrders;

/// <summary>
/// Phase 3 F1 partial / WU-18 — single-row sales-order projection over Job.
///
/// The id passed in is the underlying Job id. Returns null if the Job is not
/// at an SO-stage (order_confirmed and downstream). Mirrors
/// <see cref="GetSalesOrdersListHandler"/> for shape consistency.
/// </summary>
public record GetSalesOrderProjectionByIdQuery(int Id)
    : IRequest<SalesOrderListItemModel?>;

public class GetSalesOrderProjectionByIdHandler(AppDbContext db)
    : IRequestHandler<GetSalesOrderProjectionByIdQuery, SalesOrderListItemModel?>
{
    public async Task<SalesOrderListItemModel?> Handle(
        GetSalesOrderProjectionByIdQuery request, CancellationToken cancellationToken)
    {
        var soStages = GetSalesOrdersListHandler.SoStageCodes;

        return await db.Jobs.AsNoTracking()
            .Where(j => j.Id == request.Id && soStages.Contains(j.CurrentStage.Code))
            .Select(j => new SalesOrderListItemModel(
                j.Id,
                j.JobNumber,
                j.CustomerId ?? 0,
                j.Customer != null ? j.Customer.Name : string.Empty,
                GetSalesOrdersListHandler.MapStageCodeToSoStatus(j.CurrentStage.Code),
                null,
                j.JobParts.Count(),
                j.QuotedPrice,
                j.DueDate,
                j.CreatedAt,
                j.SalesOrderLine != null ? j.SalesOrderLine.SalesOrderId : (int?)null,
                j.Id))
            .FirstOrDefaultAsync(cancellationToken);
    }
}
