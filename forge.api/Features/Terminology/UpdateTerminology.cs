using FluentValidation;
using MediatR;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;

using Forge.Api.Hubs;
using Forge.Core.Entities;
using Forge.Core.Interfaces;
using Forge.Core.Models;
using Forge.Data.Context;

namespace Forge.Api.Features.Terminology;

public record UpdateTerminologyCommand(List<TerminologyEntryRequestModel> Entries)
    : IRequest<UpdateTerminologyResponseModel>;

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
    AppDbContext db,
    ITerminologyRepository repo,
    TerminologySpanishTranslator spanish,
    IHubContext<NotificationHub> notificationHub)
    : IRequestHandler<UpdateTerminologyCommand, UpdateTerminologyResponseModel>
{
    public async Task<UpdateTerminologyResponseModel> Handle(
        UpdateTerminologyCommand request, CancellationToken cancellationToken)
    {
        var requested = request.Entries
            .GroupBy(e => e.Key)
            .ToDictionary(g => g.Key, g => g.Last().Label?.Trim() ?? string.Empty);
        var rows = await spanish.LoadRowsAsync(requested.Keys, cancellationToken);

        var changedKeys = new HashSet<string>();
        foreach (var (key, label) in requested)
        {
            if (WriteAdminValue(rows, key, label))
                changedKeys.Add(key);
        }

        var untranslated = new List<string>();
        foreach (var term in TerminologyTermCatalog.All)
        {
            var changed = changedKeys.Contains(term.SingularKey) || changedKeys.Contains(term.PluralKey);
            var retry = (requested.ContainsKey(term.SingularKey) || requested.ContainsKey(term.PluralKey))
                && !TerminologySpanishTranslator.HasSpanishText(rows, term);
            var spanishTyped = TerminologySpanishTranslator.SpanishKeys(term)[..2]
                .Any(key => changedKeys.Contains(key) && rows[key].Label.Length > 0);
            if (!changed && !retry && !spanishTyped)
                continue;
            if (!await spanish.TranslateTermAsync(term, rows, cancellationToken))
                untranslated.Add(term.Id);
        }

        await db.SaveChangesAsync(cancellationToken);

        await notificationHub.Clients.All.SendAsync("terminologyChanged", new { }, cancellationToken);

        return new UpdateTerminologyResponseModel(await repo.GetAllAsync(cancellationToken), untranslated);
    }

    private bool WriteAdminValue(Dictionary<string, TerminologyEntry> rows, string key, string label)
    {
        if (rows.TryGetValue(key, out var row))
        {
            if (row.Label == label && row.IsAdminEdited && row.SourcePresetId is null)
                return false;
            if (label.Length == 0 && row.Label.Length == 0 && row.IsAdminEdited)
                return false;
            row.Label = label;
            row.IsAdminEdited = true;
            row.SourcePresetId = null;
            return true;
        }

        if (label.Length == 0)
            return false;

        row = new TerminologyEntry { Key = key, Label = label, IsAdminEdited = true };
        db.TerminologyEntries.Add(row);
        rows[key] = row;
        return true;
    }
}
