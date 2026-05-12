using System.Security.Claims;

using MediatR;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;

using Forge.Core.Interfaces;
using Forge.Core.Models;
using Forge.Data.Context;

namespace Forge.Api.Features.Parts;

public record UpdatePartAlternateCommand(int PartId, int AlternateId, UpdatePartAlternateRequestModel Request) : IRequest<PartAlternateResponseModel>;

public class UpdatePartAlternateHandler(AppDbContext db, IHttpContextAccessor httpContext, IClock clock)
    : IRequestHandler<UpdatePartAlternateCommand, PartAlternateResponseModel>
{
    public async Task<PartAlternateResponseModel> Handle(UpdatePartAlternateCommand request, CancellationToken cancellationToken)
    {
        var alternate = await db.PartAlternates
            .Include(a => a.AlternatePart)
            .FirstOrDefaultAsync(a => a.Id == request.AlternateId && a.PartId == request.PartId, cancellationToken)
            ?? throw new KeyNotFoundException($"Part alternate {request.AlternateId} not found");

        var userId = int.Parse(httpContext.HttpContext!.User.FindFirstValue(ClaimTypes.NameIdentifier)!);
        var r = request.Request;

        if (r.Priority.HasValue) alternate.Priority = r.Priority.Value;
        if (r.Type.HasValue) alternate.Type = r.Type.Value;
        if (r.ConversionFactor.HasValue) alternate.ConversionFactor = r.ConversionFactor.Value;
        if (r.Notes is not null) alternate.Notes = r.Notes;
        if (r.IsBidirectional.HasValue) alternate.IsBidirectional = r.IsBidirectional.Value;

        if (r.IsApproved.HasValue && r.IsApproved.Value && !alternate.IsApproved)
        {
            alternate.IsApproved = true;
            alternate.ApprovedById = userId;
            alternate.ApprovedAt = clock.UtcNow;
        }
        else if (r.IsApproved.HasValue && !r.IsApproved.Value)
        {
            alternate.IsApproved = false;
            alternate.ApprovedById = null;
            alternate.ApprovedAt = null;
        }

        await db.SaveChangesAsync(cancellationToken);

        return new PartAlternateResponseModel
        {
            Id = alternate.Id,
            PartId = alternate.PartId,
            AlternatePartId = alternate.AlternatePartId,
            AlternatePartNumber = alternate.AlternatePart.PartNumber,
            AlternatePartName = alternate.AlternatePart.Name,
            Priority = alternate.Priority,
            Type = alternate.Type,
            ConversionFactor = alternate.ConversionFactor,
            IsApproved = alternate.IsApproved,
            ApprovedAt = alternate.ApprovedAt,
            Notes = alternate.Notes,
            IsBidirectional = alternate.IsBidirectional,
            CreatedAt = alternate.CreatedAt,
        };
    }
}
