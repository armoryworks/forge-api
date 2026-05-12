using System.Text.Json;

using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;

using Forge.Api.Capabilities;
using Forge.Api.Capabilities.Discovery;
using Forge.Api.Features.Capabilities.BulkToggle;
using Forge.Api.Features.Discovery.Preview;
using Forge.Api.Services;
using Forge.Core.Entities;
using Forge.Core.Interfaces;
using Forge.Data.Context;

namespace Forge.Api.Features.Discovery.Apply;

/// <summary>
/// Phase 4 Phase-F — Apply a discovery recommendation. Persists a
/// <see cref="DiscoveryRun"/> row, computes the capability delta against
/// the chosen preset, runs the deltas through the existing bulk-toggle
/// substrate (atomic apply, validation, audit, SignalR broadcast),
/// then writes a <c>DiscoveryApplied</c> system audit row referencing the
/// run id.
///
/// The chosen preset (<see cref="ChosenPresetId"/>) may differ from the
/// engine's recommendation (the user picks an alternative). Both are
/// recorded in the run row. Per 4F Phase-F decision D2, re-running
/// discovery overwrites previous capability state — the previous
/// DiscoveryRun row stays as immutable history.
/// </summary>
public record ApplyDiscoveryRecommendationCommand(
    IReadOnlyList<DiscoveryAnswerInput> Answers,
    string ChosenPresetId,
    bool ConsultantMode,
    DateTimeOffset? StartedAt) : IRequest<DiscoveryRecommendationResponseModel>;

public class ApplyDiscoveryRecommendationValidator
    : AbstractValidator<ApplyDiscoveryRecommendationCommand>
{
    public ApplyDiscoveryRecommendationValidator()
    {
        RuleFor(x => x.Answers).NotNull();
        RuleFor(x => x.ChosenPresetId)
            .NotEmpty()
            .Matches("^PRESET-[A-Z0-9-]+$");
        RuleForEach(x => x.Answers).ChildRules(item =>
        {
            item.RuleFor(i => i.QuestionId).NotEmpty().Matches("^Q-[A-Z][0-9A-Z]+$");
            item.RuleFor(i => i.Value).MaximumLength(8000);
        });
    }
}

public class ApplyDiscoveryRecommendationHandler(
    AppDbContext db,
    ICapabilitySnapshotProvider snapshots,
    IMediator mediator,
    ISystemAuditWriter auditWriter,
    IClock clock)
    : IRequestHandler<ApplyDiscoveryRecommendationCommand, DiscoveryRecommendationResponseModel>
{
    public async Task<DiscoveryRecommendationResponseModel> Handle(
        ApplyDiscoveryRecommendationCommand request,
        CancellationToken cancellationToken)
    {
        // Validate the chosen preset is real (Custom is allowed).
        var chosenPreset = PresetCatalog.FindById(request.ChosenPresetId)
            ?? throw new ArgumentException(
                $"Unknown preset id: {request.ChosenPresetId}", nameof(request));

        // Build the answer set + run the engine to capture the recommendation
        // alongside the user's chosen preset (which may differ).
        var answerSet = new DiscoveryAnswerSet(
            request.Answers.Select(a => new DiscoveryAnswer(a.QuestionId, a.Value ?? string.Empty)));
        var recommendation = DiscoveryRecommendationEngine.Recommend(answerSet);

        // Compute deltas vs current state for the CHOSEN preset (not necessarily
        // the recommended one).
        var snapshot = snapshots.Current;
        var deltas = DiscoveryRecommendationEngine.ComputeDeltas(
            request.ChosenPresetId, snapshot.EnabledByCode);

        // Write the DiscoveryRun audit row FIRST so the bulk-toggle audit rows
        // can reference its id (via the system audit details payload).
        var actorId = db.CurrentUserId ?? 0;
        var startedAt = request.StartedAt ?? clock.UtcNow;
        var completedAt = clock.UtcNow;

        var answersJson = JsonSerializer.Serialize(
            request.Answers.Select(a => new { questionId = a.QuestionId, value = a.Value }));
        var deltasJson = JsonSerializer.Serialize(
            deltas.Select(d => new
            {
                code = d.Code,
                name = d.Name,
                currentlyEnabled = d.CurrentlyEnabled,
                willBeEnabled = d.WillBeEnabled,
            }));

        var run = new DiscoveryRun
        {
            RunByUserId = actorId,
            StartedAt = startedAt,
            CompletedAt = completedAt,
            AnswersJson = answersJson,
            RecommendedPresetId = recommendation.PresetId,
            AppliedPresetId = request.ChosenPresetId,
            RecommendedConfidence = recommendation.Confidence,
            AppliedDeltasJson = deltasJson,
            RanInConsultantMode = request.ConsultantMode,
        };
        db.DiscoveryRuns.Add(run);
        await db.SaveChangesAsync(cancellationToken);

        // Apply the deltas via the existing bulk-toggle substrate.
        // No deltas? Nothing to apply — still write the system audit row so the
        // run is recorded as having "matched current state".
        if (deltas.Count > 0)
        {
            var bulkItems = deltas
                .Select(d => new BulkToggleItem(d.Code, d.WillBeEnabled, IfMatch: null))
                .ToList();

            await mediator.Send(
                new BulkToggleCapabilitiesCommand(
                    Items: bulkItems,
                    Reason: $"Discovery run #{run.Id} → preset {request.ChosenPresetId}"),
                cancellationToken);
        }

        // Write the DiscoveryApplied system audit row.
        var auditDetails = JsonSerializer.Serialize(new
        {
            runId = run.Id,
            recommendedPresetId = recommendation.PresetId,
            appliedPresetId = request.ChosenPresetId,
            recommendedConfidence = recommendation.Confidence,
            consultantMode = request.ConsultantMode,
            deltaCount = deltas.Count,
            answerCount = request.Answers.Count,
        });
        await auditWriter.WriteAsync(
            action: "DiscoveryApplied",
            userId: actorId,
            entityType: "DiscoveryRun",
            entityId: run.Id,
            details: auditDetails,
            ct: cancellationToken);

        // Return the recommendation tuple decorated with the freshly-recomputed
        // deltas (now empty after apply) so the UI can render the success state.
        var freshDeltas = DiscoveryRecommendationEngine.ComputeDeltas(
            request.ChosenPresetId, snapshots.Current.EnabledByCode);

        return new DiscoveryRecommendationResponseModel(
            PresetId: request.ChosenPresetId,
            PresetName: chosenPreset.Name,
            PresetDescription: chosenPreset.ShortDescription,
            Confidence: recommendation.Confidence,
            ConfidenceLabel: recommendation.ConfidenceLabel,
            Rationale: recommendation.Rationale,
            Factors: recommendation.Factors
                .Select(f => new DiscoveryRecommendationFactorResponseModel(f.QuestionId, f.Description))
                .ToList(),
            Alternatives: [],
            CapabilityDeltas: freshDeltas
                .Select(d => new CapabilityDeltaResponseModel(d.Code, d.Name, d.CurrentlyEnabled, d.WillBeEnabled))
                .ToList());
    }
}
