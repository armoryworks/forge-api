using MediatR;
using Microsoft.EntityFrameworkCore;

using Forge.Core.Models;
using Forge.Data.Context;

namespace Forge.Api.Features.ComplianceForms;

public record GetUserIdentityDocumentsQuery(int UserId) : IRequest<List<IdentityDocumentResponseModel>>;

public class GetUserIdentityDocumentsHandler(AppDbContext db)
    : IRequestHandler<GetUserIdentityDocumentsQuery, List<IdentityDocumentResponseModel>>
{
    public async Task<List<IdentityDocumentResponseModel>> Handle(
        GetUserIdentityDocumentsQuery request, CancellationToken ct)
    {
        var documents = await db.IdentityDocuments
            .AsNoTracking()
            .Include(d => d.FileAttachment)
            .Where(d => d.UserId == request.UserId)
            .OrderByDescending(d => d.CreatedAt)
            .ToListAsync(ct);

        var verifierIds = documents
            .Where(d => d.VerifiedById.HasValue)
            .Select(d => d.VerifiedById!.Value)
            .Distinct()
            .ToList();

        var verifiers = verifierIds.Count > 0
            ? await db.Users.AsNoTracking()
                .Where(u => verifierIds.Contains(u.Id))
                .ToDictionaryAsync(u => u.Id, u => $"{u.LastName}, {u.FirstName}", ct)
            : new Dictionary<int, string>();

        return documents.Select(d => new IdentityDocumentResponseModel(
            d.Id, d.UserId, d.DocumentType, d.FileAttachmentId,
            d.FileAttachment.FileName, d.VerifiedAt, d.VerifiedById,
            d.VerifiedById.HasValue && verifiers.TryGetValue(d.VerifiedById.Value, out var name) ? name : null,
            d.ExpiresAt, d.Notes, d.CreatedAt
        )).ToList();
    }
}
