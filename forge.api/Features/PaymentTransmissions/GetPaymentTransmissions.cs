using MediatR;
using Microsoft.EntityFrameworkCore;

using Forge.Api.Jobs;
using Forge.Core.Enums;
using Forge.Core.Models;
using Forge.Data.Context;

namespace Forge.Api.Features.PaymentTransmissions;

public record GetPaymentTransmissionsQuery(PaymentTransmissionStatus? Status, string? SourceType)
    : IRequest<List<PaymentTransmissionListItemModel>>;

public class GetPaymentTransmissionsHandler(AppDbContext db)
    : IRequestHandler<GetPaymentTransmissionsQuery, List<PaymentTransmissionListItemModel>>
{
    public async Task<List<PaymentTransmissionListItemModel>> Handle(
        GetPaymentTransmissionsQuery request, CancellationToken cancellationToken)
    {
        var query = db.PaymentTransmissions.AsNoTracking().AsQueryable();

        if (request.Status is PaymentTransmissionStatus status)
            query = query.Where(t => t.Status == status);
        if (!string.IsNullOrWhiteSpace(request.SourceType))
            query = query.Where(t => t.SourceType == request.SourceType);

        // Materialize before projecting — enum→string ToString() isn't reliably translatable.
        var transmissions = await query.OrderByDescending(t => t.Id).ToListAsync(cancellationToken);

        return transmissions
            .Select(t => new PaymentTransmissionListItemModel(
                t.Id, t.SourceType, t.SourceId, t.Status.ToString(),
                t.AttemptCount, PaymentTransmissionJob.MaxAttempts,
                t.LastAttemptAt, t.NextAttemptAt, t.LastError, t.SubmissionRef,
                t.Amount, t.Method, t.CreatedAt))
            .ToList();
    }
}
