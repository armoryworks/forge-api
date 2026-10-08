using FluentValidation;
using MediatR;

using Forge.Core.Entities;
using Forge.Core.Models;
using Forge.Data.Context;
using Forge.Data.Extensions;

namespace Forge.Api.Features.Quality;

public record CreateQcTemplateCommand(CreateQcTemplateRequestModel Data) : IRequest<QcTemplateResponseModel>;

public class CreateQcTemplateCommandValidator : AbstractValidator<CreateQcTemplateCommand>
{
    public CreateQcTemplateCommandValidator()
    {
        RuleFor(x => x.Data.Name).NotEmpty().MaximumLength(200);
        RuleFor(x => x.Data.Description).MaximumLength(500).When(x => x.Data.Description is not null);
        RuleFor(x => x.Data.Items).NotEmpty().WithMessage("At least one checklist item is required.");
        RuleForEach(x => x.Data.Items).ChildRules(item =>
        {
            item.RuleFor(i => i.Description).NotEmpty().MaximumLength(200);
            item.RuleFor(i => i.Specification).MaximumLength(500).When(i => i.Specification is not null);
        });
    }
}

public class CreateQcTemplateHandler(AppDbContext db)
    : IRequestHandler<CreateQcTemplateCommand, QcTemplateResponseModel>
{
    public async Task<QcTemplateResponseModel> Handle(
        CreateQcTemplateCommand request, CancellationToken cancellationToken)
    {
        var data = request.Data;

        var template = new QcChecklistTemplate
        {
            Name = data.Name.Trim(),
            Description = data.Description?.Trim(),
            PartId = data.PartId,
            IsActive = true,
            Items = data.Items.Select(i => new QcChecklistItem
            {
                Description = i.Description.Trim(),
                Specification = i.Specification?.Trim(),
                SortOrder = i.SortOrder,
                IsRequired = i.IsRequired,
            }).ToList(),
        };

        db.QcChecklistTemplates.Add(template);
        await db.SaveChangesAsync(cancellationToken);

        db.LogActivityAt(
            "created",
            $"Created checklist template {template.Name} with {template.Items.Count} item{(template.Items.Count == 1 ? "" : "s")}",
            ("QcTemplate", template.Id));
        await db.SaveChangesAsync(cancellationToken);

        return await QcTemplateMapping.LoadResponseAsync(db, template.Id, cancellationToken);
    }
}
