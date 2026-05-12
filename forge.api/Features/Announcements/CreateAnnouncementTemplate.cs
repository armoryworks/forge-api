using FluentValidation;
using MediatR;

using Forge.Core.Entities;
using Forge.Core.Enums;
using Forge.Core.Models;
using Forge.Data.Context;

namespace Forge.Api.Features.Announcements;

public record CreateAnnouncementTemplateCommand(
    string Name,
    string Content,
    AnnouncementSeverity DefaultSeverity,
    AnnouncementScope DefaultScope,
    bool DefaultRequiresAcknowledgment) : IRequest<AnnouncementTemplateResponseModel>;

public class CreateAnnouncementTemplateValidator : AbstractValidator<CreateAnnouncementTemplateCommand>
{
    public CreateAnnouncementTemplateValidator()
    {
        RuleFor(x => x.Name).NotEmpty().MaximumLength(200);
        RuleFor(x => x.Content).NotEmpty().MaximumLength(5000);
    }
}

public class CreateAnnouncementTemplateHandler(AppDbContext db) : IRequestHandler<CreateAnnouncementTemplateCommand, AnnouncementTemplateResponseModel>
{
    public async Task<AnnouncementTemplateResponseModel> Handle(CreateAnnouncementTemplateCommand request, CancellationToken ct)
    {
        var template = new AnnouncementTemplate
        {
            Name = request.Name,
            Content = request.Content,
            DefaultSeverity = request.DefaultSeverity,
            DefaultScope = request.DefaultScope,
            DefaultRequiresAcknowledgment = request.DefaultRequiresAcknowledgment,
        };

        db.AnnouncementTemplates.Add(template);
        await db.SaveChangesAsync(ct);

        return new AnnouncementTemplateResponseModel(
            template.Id,
            template.Name,
            template.Content,
            template.DefaultSeverity,
            template.DefaultScope,
            template.DefaultRequiresAcknowledgment);
    }
}
