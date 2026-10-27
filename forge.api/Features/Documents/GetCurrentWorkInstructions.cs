using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;

using Forge.Core.Enums;
using Forge.Core.Models;
using Forge.Data.Context;

namespace Forge.Api.Features.Documents;

/// <summary>
/// The work instructions a shop-floor operator should follow at an operation: released WI documents linked
/// to that operation, then those linked to its part with no operation, each at its current approved
/// revision. Documents in draft, in review or obsolete are not shown.
/// </summary>
public record GetCurrentWorkInstructionsQuery(int? OperationId, int? PartId) : IRequest<List<WorkInstructionResponseModel>>;

public class GetCurrentWorkInstructionsQueryValidator : AbstractValidator<GetCurrentWorkInstructionsQuery>
{
    public GetCurrentWorkInstructionsQueryValidator()
    {
        RuleFor(x => x).Must(x => x.OperationId.HasValue || x.PartId.HasValue)
            .WithMessage("An operation or a part is required.");
    }
}

public class GetCurrentWorkInstructionsHandler(AppDbContext db)
    : IRequestHandler<GetCurrentWorkInstructionsQuery, List<WorkInstructionResponseModel>>
{
    public const string WorkInstructionCategory = "WI";

    public async Task<List<WorkInstructionResponseModel>> Handle(
        GetCurrentWorkInstructionsQuery request, CancellationToken cancellationToken)
    {
        var partId = request.PartId;
        if (request.OperationId is int opId)
            partId ??= await db.Operations.AsNoTracking()
                .Where(o => o.Id == opId)
                .Select(o => (int?)o.PartId)
                .FirstOrDefaultAsync(cancellationToken);

        var rows = await db.DocumentRevisions.AsNoTracking()
            .Where(r => r.Status == DocumentRevisionStatus.Approved
                && r.Document.Category == WorkInstructionCategory
                && r.Document.Status == ControlledDocumentStatus.Released
                && ((request.OperationId != null && r.Document.OperationId == request.OperationId)
                    || (partId != null && r.Document.PartId == partId && r.Document.OperationId == null)))
            .Select(r => new
            {
                r.DocumentId,
                r.Document.DocumentNumber,
                r.Document.Title,
                r.Document.Description,
                r.Id,
                r.RevisionNumber,
                r.FileAttachmentId,
                r.FileAttachment.FileName,
                r.FileAttachment.ContentType,
                r.ApprovedAt,
                r.Document.PartId,
                r.Document.OperationId,
            })
            .ToListAsync(cancellationToken);

        return rows
            .OrderBy(r => r.OperationId == null)
            .ThenBy(r => r.DocumentNumber)
            .Select(r => new WorkInstructionResponseModel(
                r.DocumentId, r.DocumentNumber, r.Title, r.Description, r.Id, r.RevisionNumber,
                r.FileAttachmentId, r.FileName, r.ContentType, r.ApprovedAt, r.PartId, r.OperationId))
            .ToList();
    }
}
