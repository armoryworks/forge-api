using FluentValidation;
using FluentValidation.Results;
using MediatR;
using Microsoft.EntityFrameworkCore;

using Forge.Core.Entities;
using Forge.Core.Models;
using Forge.Data.Context;
using Forge.Data.Extensions;

namespace Forge.Api.Features.Quality;

public record UpdateQcTemplateCommand(int Id, UpdateQcTemplateRequestModel Data) : IRequest<QcTemplateResponseModel>;

public class UpdateQcTemplateCommandValidator : AbstractValidator<UpdateQcTemplateCommand>
{
    public UpdateQcTemplateCommandValidator()
    {
        RuleFor(x => x.Data.Name).NotEmpty().MaximumLength(200);
        RuleFor(x => x.Data.Description).MaximumLength(500).When(x => x.Data.Description is not null);
        RuleFor(x => x.Data.Items).NotEmpty().WithMessage("At least one checklist item is required.");
        RuleFor(x => x.Data.Items)
            .Must(items => items.Where(i => i.Id.HasValue).GroupBy(i => i.Id).All(g => g.Count() == 1))
            .WithMessage("Each checklist item can appear only once.")
            .When(x => x.Data.Items is not null);
        RuleForEach(x => x.Data.Items).ChildRules(item =>
        {
            item.RuleFor(i => i.Description).NotEmpty().MaximumLength(200);
            item.RuleFor(i => i.Specification).MaximumLength(500).When(i => i.Specification is not null);
        });
    }
}

public class UpdateQcTemplateHandler(AppDbContext db)
    : IRequestHandler<UpdateQcTemplateCommand, QcTemplateResponseModel>
{
    public async Task<QcTemplateResponseModel> Handle(
        UpdateQcTemplateCommand request, CancellationToken cancellationToken)
    {
        var template = await db.QcChecklistTemplates
            .Include(t => t.Items)
            .FirstOrDefaultAsync(t => t.Id == request.Id, cancellationToken)
            ?? throw new KeyNotFoundException($"Checklist template {request.Id} not found.");

        var data = request.Data;
        var partId = data.PartId is > 0 ? data.PartId : null;
        var keptIds = data.Items.Where(i => i.Id.HasValue).Select(i => i.Id!.Value).ToHashSet();

        var failures = new List<ValidationFailure>();
        if (partId is int part && !await db.Parts.AnyAsync(p => p.Id == part, cancellationToken))
            failures.Add(new ValidationFailure("partId", "Pick a part that exists.") { AttemptedValue = part });

        var unknown = keptIds.Where(id => template.Items.All(i => i.Id != id)).ToList();
        if (unknown.Count > 0)
            failures.Add(new ValidationFailure(
                "items",
                $"Checklist template {template.Id} has no item {string.Join(", ", unknown)}."));

        if (failures.Count > 0)
            throw new ValidationException(failures);

        var changedFields = new List<string>();

        var name = data.Name.Trim();
        if (name != template.Name)
        {
            template.Name = name;
            changedFields.Add("name");
        }

        var description = data.Description?.Trim();
        if (description != template.Description)
        {
            template.Description = description;
            changedFields.Add("description");
        }

        if (partId != template.PartId)
        {
            template.PartId = partId;
            changedFields.Add("part");
        }

        var itemChanges = await ApplyItemsAsync(template, data.Items, keptIds, cancellationToken);
        if (itemChanges is not null)
            changedFields.Add($"items ({itemChanges})");

        if (changedFields.Count > 0)
        {
            db.LogActivityAt(
                "updated",
                $"Updated {changedFields.Count} field{(changedFields.Count == 1 ? "" : "s")}: {string.Join(", ", changedFields)}",
                ("QcTemplate", template.Id));
            await db.SaveChangesAsync(cancellationToken);
        }

        return await QcTemplateMapping.LoadResponseAsync(db, template.Id, cancellationToken);
    }

    private async Task<string?> ApplyItemsAsync(
        QcChecklistTemplate template, List<UpdateQcTemplateItemModel> incoming, HashSet<int> keptIds, CancellationToken ct)
    {
        var removed = template.Items.Where(i => !keptIds.Contains(i.Id)).ToList();
        if (removed.Count > 0)
        {
            var removedIds = removed.Select(i => i.Id).ToList();
            var linkedResults = await db.QcInspectionResults
                .Where(r => r.ChecklistItemId != null && removedIds.Contains(r.ChecklistItemId.Value))
                .ToListAsync(ct);
            foreach (var result in linkedResults)
                result.ChecklistItemId = null;

            foreach (var item in removed)
            {
                template.Items.Remove(item);
                db.QcChecklistItems.Remove(item);
            }
        }

        var added = 0;
        var updated = 0;
        foreach (var model in incoming)
        {
            var description = model.Description.Trim();
            var specification = model.Specification?.Trim();

            if (model.Id is not int id)
            {
                template.Items.Add(new QcChecklistItem
                {
                    Description = description,
                    Specification = specification,
                    SortOrder = model.SortOrder,
                    IsRequired = model.IsRequired,
                });
                added++;
                continue;
            }

            var item = template.Items.First(i => i.Id == id);
            if (item.Description == description && item.Specification == specification
                && item.SortOrder == model.SortOrder && item.IsRequired == model.IsRequired)
                continue;

            item.Description = description;
            item.Specification = specification;
            item.SortOrder = model.SortOrder;
            item.IsRequired = model.IsRequired;
            updated++;
        }

        var parts = new List<string>();
        if (added > 0) parts.Add($"{added} added");
        if (updated > 0) parts.Add($"{updated} changed");
        if (removed.Count > 0) parts.Add($"{removed.Count} removed");
        return parts.Count > 0 ? string.Join(", ", parts) : null;
    }
}
