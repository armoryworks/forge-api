using Microsoft.EntityFrameworkCore;

using Forge.Data.Context;

namespace Forge.Api.Features.Deliverables;

public static class DeliverableTypes
{
    public const string GroupCode = "deliverable_type";
    public const string UnknownTypeMessage = "DeliverableTypeId must be an active deliverable type.";

    public static Task<bool> ExistsAsync(AppDbContext db, int id, CancellationToken ct) =>
        db.ReferenceData.AnyAsync(r => r.Id == id && r.GroupCode == GroupCode && r.IsActive, ct);
}
