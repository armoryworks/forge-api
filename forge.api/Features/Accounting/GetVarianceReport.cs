using MediatR;
using Microsoft.EntityFrameworkCore;

using Forge.Core.Models.Accounting;
using Forge.Data.Context;

namespace Forge.Api.Features.Accounting;

/// <summary>Standard-cost variance rollup for a date range (the six slots + residual; lumped = sum).</summary>
public record GetVarianceReportQuery(DateOnly From, DateOnly To) : IRequest<VarianceReportModel>;

public class GetVarianceReportHandler(AppDbContext db, IVarianceReportService service)
    : IRequestHandler<GetVarianceReportQuery, VarianceReportModel>
{
    public async Task<VarianceReportModel> Handle(GetVarianceReportQuery request, CancellationToken cancellationToken)
    {
        var bookId = await db.Books.AsNoTracking()
            .Where(b => b.IsActive).OrderBy(b => b.Id).Select(b => b.Id).FirstOrDefaultAsync(cancellationToken);
        if (bookId == 0)
            throw new KeyNotFoundException("No active accounting book is configured.");

        return await service.GetAsync(bookId, request.From, request.To, cancellationToken);
    }
}
