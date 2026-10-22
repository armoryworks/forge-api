using FluentValidation;
using MediatR;

using Forge.Core.Enums;
using Forge.Core.Interfaces;
using Forge.Core.Models;

namespace Forge.Api.Features.Parts;

public record UpdatePartStepGatingCommand(int PartId, UpdatePartStepGatingRequestModel Data) : IRequest;

public class UpdatePartStepGatingValidator : AbstractValidator<UpdatePartStepGatingCommand>
{
    public UpdatePartStepGatingValidator()
    {
        RuleFor(x => x.PartId).GreaterThan(0);
        RuleFor(x => x.Data.StepGating).IsInEnum().When(x => x.Data.StepGating.HasValue);
    }
}

public class UpdatePartStepGatingHandler(IPartRepository repo) : IRequestHandler<UpdatePartStepGatingCommand>
{
    public async Task Handle(UpdatePartStepGatingCommand request, CancellationToken cancellationToken)
    {
        var part = await repo.FindAsync(request.PartId, cancellationToken)
            ?? throw new KeyNotFoundException($"Part {request.PartId} not found");

        var next = request.Data.StepGating;
        if (part.StepGating == next)
            return;

        var before = Label(part.StepGating);
        part.StepGating = next;
        repo.LogPartActivity(part.Id, "step-gating-changed", $"Step gating changed from {before} to {Label(next)}");
        await repo.SaveChangesAsync(cancellationToken);
    }

    private static string Label(StepGatingMode? mode) => mode?.ToString() ?? "the shop setting";
}
