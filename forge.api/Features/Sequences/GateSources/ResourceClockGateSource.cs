using Microsoft.EntityFrameworkCore;

using Forge.Core.Enums;
using Forge.Core.Sequences;
using Forge.Data.Context;

namespace Forge.Api.Features.Sequences.GateSources;

/// <summary>
/// Go while the referenced resource has no expired clock. Config: <c>{ "resourceType", "resourceId" }</c> or
/// <c>{ "fromSubject": true }</c> to use the instance's subject. A resource with no clock at all is Go (nothing to
/// expire); an expired clock whose action is Flag does not block.
/// </summary>
public class ResourceClockGateSource(AppDbContext db) : IGateSource
{
    public SequenceGateSourceType SourceType => SequenceGateSourceType.ResourceClock;

    public string? CustomKey => null;

    public async Task<SequenceGateVerdictResult> EvaluateAsync(SequenceGateContext context, CancellationToken cancellationToken)
    {
        var cfg = SequenceGateConfig.Parse(context.Gate.ConfigJson);
        var type = cfg.GetBool("fromSubject") ? context.Instance.SubjectEntityType : cfg.GetString("resourceType");
        var id = cfg.GetBool("fromSubject") ? context.Instance.SubjectEntityId : cfg.GetInt("resourceId");
        if (string.IsNullOrEmpty(type) || id is null)
            return SequenceGateVerdictResult.NoGo("Gate config names no resource");

        var clocks = await db.SequenceResourceClocks
            .Where(c => c.ResourceType == type && c.ResourceId == id && c.DeletedAt == null)
            .ToListAsync(cancellationToken);
        var expired = clocks.Where(c => c.ExpiresAt <= context.Now && c.ExpiryAction != SequenceExpiryAction.Flag).ToList();
        if (expired.Count > 0)
            return SequenceGateVerdictResult.NoGo($"{type} {id} expired {expired.Min(c => c.ExpiresAt):u}");
        var next = clocks.Where(c => c.ExpiresAt > context.Now).OrderBy(c => c.ExpiresAt).FirstOrDefault();
        return SequenceGateVerdictResult.Go(next is null ? null : $"Expires {next.ExpiresAt:u}");
    }
}
