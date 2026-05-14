using System.Security.Claims;

using MediatR;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Forge.Core.Models;
using Forge.Data.Context;

namespace Forge.Api.Features.Documents;

public record GetControlledDocumentsQuery(
    string? Category,
    ControlledDocumentStatus? Status) : IRequest<List<ControlledDocumentResponseModel>>;

public class GetControlledDocumentsHandler(
    AppDbContext db,
    IHttpContextAccessor httpContextAccessor) : IRequestHandler<GetControlledDocumentsQuery, List<ControlledDocumentResponseModel>>
{
    public async Task<List<ControlledDocumentResponseModel>> Handle(GetControlledDocumentsQuery request, CancellationToken cancellationToken)
    {
        _ = httpContextAccessor.HttpContext?.User.FindFirstValue(ClaimTypes.NameIdentifier)
            ?? throw new UnauthorizedAccessException("User not authenticated.");

        var query = db.ControlledDocuments
            .Include(d => d.Revisions)
            .AsNoTracking()
            .AsQueryable();

        if (!string.IsNullOrWhiteSpace(request.Category))
            query = query.Where(d => d.Category == request.Category);

        if (request.Status.HasValue)
            query = query.Where(d => d.Status == request.Status.Value);

        var documents = await query
            .OrderByDescending(d => d.UpdatedAt)
            .ToListAsync(cancellationToken);

        return documents.Select(d => new ControlledDocumentResponseModel(
            d.Id,
            d.DocumentNumber,
            d.Title,
            d.Description,
            d.Category,
            d.CurrentRevision,
            d.Status,
            d.OwnerId,
            d.CheckedOutById,
            d.CheckedOutAt,
            d.ReleasedAt,
            d.ReviewDueDate,
            d.ReviewIntervalDays,
            d.Revisions.Count,
            d.CreatedAt,
            d.UpdatedAt)).ToList();
    }
}
