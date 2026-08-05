using MediatR;
using Microsoft.EntityFrameworkCore;

using Forge.Core.Interfaces;
using Forge.Data.Context;
using Forge.Data.Extensions;

namespace Forge.Api.Features.I18nLabelOverrides;

public record RevertI18nLabelOverrideCommand(int Id) : IRequest;

/// <summary>
/// Reverts one key + language back to the shipped default (soft delete).
/// Reverting a human override also removes the machine translations that were
/// derived from it — they translate text that no longer exists. Machine rows
/// reverted directly only remove themselves.
/// </summary>
public class RevertI18nLabelOverrideHandler(AppDbContext db, IClock clock)
    : IRequestHandler<RevertI18nLabelOverrideCommand>
{
    public async Task Handle(RevertI18nLabelOverrideCommand request, CancellationToken cancellationToken)
    {
        var entity = await db.I18nLabelOverrides
            .FirstOrDefaultAsync(o => o.Id == request.Id, cancellationToken)
            ?? throw new KeyNotFoundException($"I18nLabelOverride {request.Id} not found");

        entity.DeletedAt = clock.UtcNow;

        var derivedCount = 0;
        if (!entity.IsMachineTranslated)
        {
            var derived = await db.I18nLabelOverrides
                .Where(o => o.Key == entity.Key
                    && o.Id != entity.Id
                    && o.IsMachineTranslated
                    && o.SourceLanguageCode == entity.LanguageCode)
                .ToListAsync(cancellationToken);
            foreach (var row in derived)
            {
                row.DeletedAt = clock.UtcNow;
            }
            derivedCount = derived.Count;
        }

        var derivedSuffix = derivedCount > 0 ? $" (+{derivedCount} derived translation(s))" : string.Empty;
        db.LogActivityAt(
            "deleted",
            $"Reverted label '{entity.Key}' ({entity.LanguageCode}) to default{derivedSuffix}",
            ("I18nLabelOverride", entity.Id));

        await db.SaveChangesAsync(cancellationToken);
    }
}
