using MediatR;

using Microsoft.EntityFrameworkCore;

using Forge.Api.Capabilities;
using Forge.Core.Interfaces;
using Forge.Data.Context;
using Forge.Data.Extensions;

namespace Forge.Api.Features.Costing.Tier3;

/// <summary>Soft-deletes a user template. System templates are never deletable.</summary>
[RequiresCapability("CAP-COSTING-TIER3-ABC")]
public record DeleteCostingTemplateCommand(int Id) : IRequest;

public class DeleteCostingTemplateHandler(AppDbContext db, IClock clock)
    : IRequestHandler<DeleteCostingTemplateCommand>
{
    public async Task Handle(DeleteCostingTemplateCommand request, CancellationToken ct)
    {
        var template = await db.CostingTemplates
            .FirstOrDefaultAsync(t => t.Id == request.Id, ct)
            ?? throw new KeyNotFoundException($"Costing template {request.Id} not found.");

        if (template.IsSystem)
            throw new InvalidOperationException("System costing templates cannot be deleted.");

        template.DeletedAt = clock.UtcNow;
        db.LogActivityAt("deleted", $"Costing template '{template.Name}'", ("CostingTemplate", template.Id));
        await db.SaveChangesAsync(ct);
    }
}
