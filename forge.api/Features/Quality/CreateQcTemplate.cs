using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;

using Forge.Core.Entities;
using Forge.Core.Models;
using Forge.Data.Context;

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

        var created = await db.QcChecklistTemplates
            .AsNoTracking()
            .Include(t => t.Items)
            .Include(t => t.Part)
            .Where(t => t.Id == template.Id)
            .Select(t => new QcTemplateResponseModel(
                t.Id,
                t.Name,
                t.Description,
                t.PartId,
                t.Part != null ? t.Part.PartNumber : null,
                t.IsActive,
                t.Items.OrderBy(i => i.SortOrder).Select(i => new QcTemplateItemModel(
                    i.Id,
                    i.Description,
                    i.Specification,
                    i.SortOrder,
                    i.IsRequired
                )).ToList()))
            .FirstAsync(cancellationToken);

        return created;
    }
}
