using MediatR;
using Microsoft.EntityFrameworkCore;

using Forge.Core.Enums;
using Forge.Core.Models;
using Forge.Data.Context;

namespace Forge.Api.Features.ComplianceForms;

/// <summary>
/// Response model for admin I-9 pending queue — submissions awaiting employer Section 2.
/// </summary>
public record I9PendingItemResponseModel(
    int SubmissionId,
    int UserId,
    string UserName,
    string UserEmail,
    DateTimeOffset? Section1SignedAt,
    DateTimeOffset? Section2OverdueAt,
    bool IsOverdue,
    string? DocuSealSubmitUrl);

public record GetI9PendingQuery : IRequest<List<I9PendingItemResponseModel>>;

public class GetI9PendingHandler(AppDbContext db)
    : IRequestHandler<GetI9PendingQuery, List<I9PendingItemResponseModel>>
{
    public async Task<List<I9PendingItemResponseModel>> Handle(
        GetI9PendingQuery request, CancellationToken ct)
    {
        var now = DateTimeOffset.UtcNow;

        var items = await db.ComplianceFormSubmissions
            .AsNoTracking()
            .Include(s => s.Template)
            .Where(s =>
                s.Template.FormType == ComplianceFormType.I9
                && s.I9Section1SignedAt != null
                && s.I9Section2SignedAt == null
                && s.Status != ComplianceSubmissionStatus.Completed)
            .Join(db.Users,
                s => s.UserId,
                u => u.Id,
                (s, u) => new { s, u })
            .OrderBy(x => x.s.I9Section2OverdueAt)
            .Select(x => new I9PendingItemResponseModel(
                x.s.Id,
                x.s.UserId,
                x.u.LastName + ", " + x.u.FirstName,
                x.u.Email!,
                x.s.I9Section1SignedAt,
                x.s.I9Section2OverdueAt,
                x.s.I9Section2OverdueAt != null && x.s.I9Section2OverdueAt < now,
                x.s.DocuSealSubmitUrl))
            .ToListAsync(ct);

        return items;
    }
}
