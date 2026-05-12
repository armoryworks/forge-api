using MediatR;
using Microsoft.EntityFrameworkCore;
using Forge.Core.Models;
using Forge.Data.Context;

namespace Forge.Api.Features.Admin;

public record GetEmployeeDocumentsQuery(int UserId) : IRequest<List<EmployeeDocumentResponseModel>>;

public class GetEmployeeDocumentsHandler(AppDbContext db) : IRequestHandler<GetEmployeeDocumentsQuery, List<EmployeeDocumentResponseModel>>
{
    public async Task<List<EmployeeDocumentResponseModel>> Handle(GetEmployeeDocumentsQuery request, CancellationToken ct)
    {
        return await db.FileAttachments
            .Where(f => f.EntityType == "Employee" && f.EntityId == request.UserId)
            .OrderByDescending(f => f.CreatedAt)
            .Select(f => new EmployeeDocumentResponseModel(
                f.Id, f.FileName, f.ContentType, f.Size, f.DocumentType, f.ExpirationDate, f.CreatedAt))
            .ToListAsync(ct);
    }
}
