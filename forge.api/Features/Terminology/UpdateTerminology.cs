using FluentValidation;
using MediatR;
using Microsoft.AspNetCore.SignalR;
using Forge.Api.Hubs;
using Forge.Core.Entities;
using Forge.Core.Interfaces;
using Forge.Core.Models;

namespace Forge.Api.Features.Terminology;

public record UpdateTerminologyCommand(List<TerminologyEntryRequestModel> Entries)
    : IRequest<List<TerminologyEntryResponseModel>>;

public class UpdateTerminologyValidator : AbstractValidator<UpdateTerminologyCommand>
{
    public UpdateTerminologyValidator()
    {
        RuleFor(x => x.Entries).NotEmpty().WithMessage("At least one terminology entry is required");
        RuleForEach(x => x.Entries).ChildRules(entry =>
        {
            entry.RuleFor(e => e.Key).NotEmpty().MaximumLength(100)
                .Matches("^[a-z][a-z0-9_]*(:[a-z]{2})?$")
                .WithMessage("Terminology keys are lower-case snake_case with an optional ':<language>' suffix");
            entry.RuleFor(e => e.Label).MaximumLength(200);
        });
    }
}

public class UpdateTerminologyHandler(
    ITerminologyRepository repo,
    IHubContext<NotificationHub> notificationHub)
    : IRequestHandler<UpdateTerminologyCommand, List<TerminologyEntryResponseModel>>
{
    public async Task<List<TerminologyEntryResponseModel>> Handle(
        UpdateTerminologyCommand request, CancellationToken cancellationToken)
    {
        foreach (var entry in request.Entries)
        {
            var label = entry.Label?.Trim() ?? string.Empty;
            var existing = await repo.FindByKeyAsync(entry.Key, cancellationToken);

            if (label.Length == 0)
            {
                if (existing is not null)
                    repo.Remove(existing);
                continue;
            }

            if (existing is not null)
            {
                if (existing.Label == label)
                    continue;
                existing.Label = label;
                existing.IsAdminEdited = true;
                existing.SourcePresetId = null;
            }
            else
            {
                await repo.AddAsync(new TerminologyEntry
                {
                    Key = entry.Key,
                    Label = label,
                    IsAdminEdited = true,
                }, cancellationToken);
            }
        }

        await repo.SaveChangesAsync(cancellationToken);

        await notificationHub.Clients.All.SendAsync("terminologyChanged", new { }, cancellationToken);

        return await repo.GetAllAsync(cancellationToken);
    }
}
