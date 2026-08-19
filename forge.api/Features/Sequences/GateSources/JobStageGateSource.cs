using Microsoft.EntityFrameworkCore;

using Forge.Core.Enums;
using Forge.Core.Sequences;
using Forge.Data.Context;

namespace Forge.Api.Features.Sequences.GateSources;

/// <summary>
/// First job-structure tie-in. Custom key <c>job-stage</c>: Go once the subject Job has reached the configured
/// kanban stage — config <c>{ "key": "job-stage", "stageCode": "in-production" }</c> (or <c>"stageId"</c>) — i.e. the
/// job's current stage sort order is at or past that stage's within the same track. Re-evaluated on
/// <c>JobStageChangedEvent</c>.
/// </summary>
public class JobStageGateSource(AppDbContext db) : IGateSource
{
    public const string Key = "job-stage";

    public SequenceGateSourceType SourceType => SequenceGateSourceType.Custom;

    public string? CustomKey => Key;

    public async Task<SequenceGateVerdictResult> EvaluateAsync(SequenceGateContext context, CancellationToken cancellationToken)
    {
        if (context.Instance.SubjectEntityType != "Job" || context.Instance.SubjectEntityId is null)
            return SequenceGateVerdictResult.NoGo("job-stage gate needs a Job subject");
        var cfg = SequenceGateConfig.Parse(context.Gate.ConfigJson);
        var stageCode = cfg.GetString("stageCode");
        var stageId = cfg.GetInt("stageId");
        if (stageCode is null && stageId is null) return SequenceGateVerdictResult.NoGo("job-stage gate config names no stage");

        var job = await db.Jobs.Include(j => j.CurrentStage)
            .FirstOrDefaultAsync(j => j.Id == context.Instance.SubjectEntityId, cancellationToken);
        if (job is null) return SequenceGateVerdictResult.NoGo("Job not found");

        var target = stageId.HasValue
            ? await db.JobStages.FirstOrDefaultAsync(s => s.Id == stageId, cancellationToken)
            : await db.JobStages.FirstOrDefaultAsync(s => s.TrackTypeId == job.TrackTypeId && s.Code == stageCode, cancellationToken);
        if (target is null) return SequenceGateVerdictResult.NoGo($"Stage '{stageCode ?? stageId.ToString()}' not found on this track");

        return job.CurrentStage.SortOrder >= target.SortOrder
            ? SequenceGateVerdictResult.Go($"Job at {job.CurrentStage.Name}")
            : SequenceGateVerdictResult.NoGo($"Job at {job.CurrentStage.Name}; needs {target.Name}");
    }
}
