using MediatR;
using Microsoft.EntityFrameworkCore;

using Forge.Core.Models;
using Forge.Data.Context;

namespace Forge.Api.Features.Quality;

public record GetPpapSubmissionQuery(int Id) : IRequest<PpapSubmissionResponseModel>;

public class GetPpapSubmissionHandler(AppDbContext db)
    : IRequestHandler<GetPpapSubmissionQuery, PpapSubmissionResponseModel>
{
    public async Task<PpapSubmissionResponseModel> Handle(
        GetPpapSubmissionQuery request, CancellationToken cancellationToken)
    {
        var s = await db.PpapSubmissions
            .AsNoTracking()
            .Include(x => x.Part)
            .Include(x => x.Customer)
            .Include(x => x.Elements)
            .FirstOrDefaultAsync(x => x.Id == request.Id, cancellationToken)
            ?? throw new KeyNotFoundException($"PPAP submission {request.Id} not found");

        var assigneeIds = s.Elements
            .Where(e => e.AssignedToUserId.HasValue)
            .Select(e => e.AssignedToUserId!.Value)
            .Distinct()
            .ToList();

        var userNames = new Dictionary<int, string>();
        if (s.PswSignedByUserId.HasValue)
            assigneeIds.Add(s.PswSignedByUserId.Value);

        if (assigneeIds.Count > 0)
        {
            userNames = await db.Users
                .Where(u => assigneeIds.Contains(u.Id))
                .ToDictionaryAsync(u => u.Id, u => $"{u.LastName}, {u.FirstName}", cancellationToken);
        }

        var attachmentCounts = await db.FileAttachments
            .Where(f => f.EntityType == "PpapElement" && s.Elements.Select(e => e.Id).Contains(f.EntityId))
            .GroupBy(f => f.EntityId)
            .ToDictionaryAsync(g => g.Key, g => g.Count(), cancellationToken);

        return new PpapSubmissionResponseModel
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
            PswSignedByName = s.PswSignedByUserId.HasValue && userNames.TryGetValue(s.PswSignedByUserId.Value, out var sn) ? sn : null,
            PswSignedAt = s.PswSignedAt,
            CompletedElements = s.Elements.Count(e => e.Status == PpapElementStatus.Complete),
            RequiredElements = s.Elements.Count(e => e.IsRequired),
            Elements = s.Elements.OrderBy(e => e.ElementNumber).Select(e => new PpapElementResponseModel
            {
                Id = e.Id,
                ElementNumber = e.ElementNumber,
                ElementName = e.ElementName,
                Status = e.Status,
                IsRequired = e.IsRequired,
                Notes = e.Notes,
                AssignedToName = e.AssignedToUserId.HasValue && userNames.TryGetValue(e.AssignedToUserId.Value, out var n) ? n : null,
                CompletedAt = e.CompletedAt,
                AttachmentCount = attachmentCounts.GetValueOrDefault(e.Id, 0),
            }).ToList(),
            CreatedAt = s.CreatedAt,
        };
    }
}
