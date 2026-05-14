using MediatR;
using Microsoft.EntityFrameworkCore;

using Forge.Core.Models;
using Forge.Data.Context;

namespace Forge.Api.Features.Quality;

public record GetPpapSubmissionsQuery(int? PartId, int? CustomerId, PpapStatus? Status) : IRequest<List<PpapSubmissionResponseModel>>;

public class GetPpapSubmissionsHandler(AppDbContext db)
    : IRequestHandler<GetPpapSubmissionsQuery, List<PpapSubmissionResponseModel>>
{
    public async Task<List<PpapSubmissionResponseModel>> Handle(
        GetPpapSubmissionsQuery request, CancellationToken cancellationToken)
    {
        var query = db.PpapSubmissions
            .AsNoTracking()
            .Include(s => s.Part)
            .Include(s => s.Customer)
            .Include(s => s.Elements)
            .AsQueryable();

        if (request.PartId.HasValue)
            query = query.Where(s => s.PartId == request.PartId.Value);
        if (request.CustomerId.HasValue)
            query = query.Where(s => s.CustomerId == request.CustomerId.Value);
        if (request.Status.HasValue)
            query = query.Where(s => s.Status == request.Status.Value);

        var signerIds = await query
            .Where(s => s.PswSignedByUserId.HasValue)
            .Select(s => s.PswSignedByUserId!.Value)
            .Distinct()
            .ToListAsync(cancellationToken);

        var signerNames = await db.Users
            .Where(u => signerIds.Contains(u.Id))
            .ToDictionaryAsync(u => u.Id, u => $"{u.LastName}, {u.FirstName}", cancellationToken);

        var submissions = await query
            .OrderByDescending(s => s.CreatedAt)
            .ToListAsync(cancellationToken);

        return submissions.Select(s => new PpapSubmissionResponseModel
        {
            Id = s.Id,
            SubmissionNumber = s.SubmissionNumber,
            PartId = s.PartId,
            PartNumber = s.Part.PartNumber,
            PartDescription = s.Part.Description ?? string.Empty,
            CustomerId = s.CustomerId,
            CustomerName = s.Customer.Name,
            PpapLevel = s.PpapLevel,
            Status = s.Status,
            Reason = s.Reason,
            PartRevision = s.PartRevision,
            SubmittedAt = s.SubmittedAt,
            ApprovedAt = s.ApprovedAt,
            DueDate = s.DueDate,
            CustomerContactName = s.CustomerContactName,
            CustomerResponseNotes = s.CustomerResponseNotes,
            InternalNotes = s.InternalNotes,
            PswSignedByName = s.PswSignedByUserId.HasValue && signerNames.TryGetValue(s.PswSignedByUserId.Value, out var name) ? name : null,
            PswSignedAt = s.PswSignedAt,
            CompletedElements = s.Elements.Count(e => e.Status == PpapElementStatus.Complete),
            RequiredElements = s.Elements.Count(e => e.IsRequired),
            Elements = [],
            CreatedAt = s.CreatedAt,
        }).ToList();
    }
}
