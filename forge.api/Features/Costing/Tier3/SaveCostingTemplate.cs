using FluentValidation;

using MediatR;

using Microsoft.EntityFrameworkCore;

using Forge.Api.Capabilities;
using Forge.Core.Entities.Costing;
using Forge.Core.Enums;
using Forge.Core.Models.Costing;
using Forge.Data.Context;
using Forge.Data.Extensions;

namespace Forge.Api.Features.Costing.Tier3;

/// <summary>
/// Creates (null id) or replaces (id) a costing template with its whole line
/// graph — the same shape the editor holds. System templates stay system and
/// keep their name-editable-but-never-deletable contract.
/// </summary>
[RequiresCapability("CAP-COSTING-TIER3-ABC")]
public record SaveCostingTemplateCommand(SaveCostingTemplateRequestModel Model)
    : IRequest<CostingTemplateResponseModel>;

public class SaveCostingTemplateValidator : AbstractValidator<SaveCostingTemplateCommand>
{
    public SaveCostingTemplateValidator()
    {
        RuleFor(x => x.Model.Name).NotEmpty().MaximumLength(128);
        RuleFor(x => x.Model.Description).MaximumLength(512);
        RuleFor(x => x.Model.Lines).NotEmpty().WithMessage("A template needs at least one line.");
        RuleFor(x => x.Model.Lines)
            .Must(lines => lines.Select(l => l.Code.Trim().ToUpperInvariant()).Distinct().Count() == lines.Count)
            .WithMessage("Line codes must be unique within the template.");
        RuleForEach(x => x.Model.Lines).ChildRules(line =>
        {
            line.RuleFor(l => l.Code).NotEmpty().MaximumLength(32)
                .Matches("^[A-Za-z0-9][A-Za-z0-9-_]*$")
                .WithMessage("Line codes are letters, numbers, hyphens, underscores.");
            line.RuleFor(l => l.Name).NotEmpty().MaximumLength(128);
            line.RuleFor(l => l.GlAccountNumber).MaximumLength(16);
            line.RuleFor(l => l.GlAccountName).MaximumLength(128);
            line.RuleFor(l => l.GlAccountName).NotEmpty()
                .When(l => !string.IsNullOrWhiteSpace(l.GlAccountNumber))
                .WithMessage("A GL account number needs a name for first-time creation.");
        });
    }
}

public class SaveCostingTemplateHandler(AppDbContext db)
    : IRequestHandler<SaveCostingTemplateCommand, CostingTemplateResponseModel>
{
    public async Task<CostingTemplateResponseModel> Handle(SaveCostingTemplateCommand request, CancellationToken ct)
    {
        var m = request.Model;
        var name = m.Name.Trim();

        var duplicate = await db.CostingTemplates
            .AnyAsync(t => t.Name == name && t.Id != (m.Id ?? 0), ct);
        if (duplicate)
            throw new InvalidOperationException($"A costing template named '{name}' already exists.");

        CostingTemplate template;
        if (m.Id is { } id)
        {
            template = await db.CostingTemplates.Include(t => t.Lines)
                .FirstOrDefaultAsync(t => t.Id == id, ct)
                ?? throw new KeyNotFoundException($"Costing template {id} not found.");
            template.Name = name;
            template.Description = m.Description;
            db.CostingTemplateLines.RemoveRange(template.Lines);
            template.Lines.Clear();
        }
        else
        {
            template = new CostingTemplate { Name = name, Description = m.Description };
            db.CostingTemplates.Add(template);
        }

        var sort = 0;
        foreach (var line in m.Lines)
        {
            template.Lines.Add(new CostingTemplateLine
            {
                Code = line.Code.Trim().ToUpperInvariant(),
                Name = line.Name.Trim(),
                Behavior = line.Behavior,
                Driver = line.Driver,
                AmountBasis = line.AmountBasis,
                DefaultValue = line.DefaultValue,
                GlAccountNumber = string.IsNullOrWhiteSpace(line.GlAccountNumber) ? null : line.GlAccountNumber.Trim(),
                GlAccountName = string.IsNullOrWhiteSpace(line.GlAccountName) ? null : line.GlAccountName.Trim(),
                SortOrder = sort++,
            });
        }

        await db.SaveChangesAsync(ct);

        db.LogActivityAt(m.Id is null ? "created" : "updated",
            $"Costing template '{name}' ({template.Lines.Count} lines)",
            ("CostingTemplate", template.Id));
        await db.SaveChangesAsync(ct);

        return new CostingTemplateResponseModel(
            template.Id, template.Name, template.Description, template.IsSystem,
            template.Lines.OrderBy(l => l.SortOrder).Select(l => new CostingTemplateLineModel(
                l.Id, l.Code, l.Name, l.Behavior, l.Driver, l.AmountBasis,
                l.DefaultValue, l.GlAccountNumber, l.GlAccountName, l.SortOrder)).ToList());
    }
}
