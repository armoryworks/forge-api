using MediatR;
using Microsoft.EntityFrameworkCore;

using Forge.Api.Capabilities;
using Forge.Core.Interfaces;
using Forge.Core.Models.Accounting;
using Forge.Data.Context;

namespace Forge.Api.Features.Accounting;

/// <summary>Replace a journal template's header and lines. CAP-ACCT-FULLGL gated.</summary>
[RequiresCapability("CAP-ACCT-FULLGL")]
public record UpdateJournalTemplateCommand(int TemplateId, UpdateJournalTemplateModel Model) : IRequest<JournalTemplateModel>;

public class UpdateJournalTemplateHandler(IJournalTemplateService service)
    : IRequestHandler<UpdateJournalTemplateCommand, JournalTemplateModel>
{
    public Task<JournalTemplateModel> Handle(UpdateJournalTemplateCommand request, CancellationToken ct)
        => service.UpdateAsync(request.TemplateId, request.Model, ct);
}

/// <summary>
/// Soft-deletes a journal template (stamps <c>DeletedAt</c>); entries already posted from it are untouched.
/// CAP-ACCT-FULLGL gated.
/// </summary>
[RequiresCapability("CAP-ACCT-FULLGL")]
public record DeleteJournalTemplateCommand(int TemplateId) : IRequest;

public class DeleteJournalTemplateHandler(AppDbContext db, IClock clock) : IRequestHandler<DeleteJournalTemplateCommand>
{
    public async Task Handle(DeleteJournalTemplateCommand request, CancellationToken ct)
    {
        var template = await db.JournalTemplates.FirstOrDefaultAsync(t => t.Id == request.TemplateId, ct)
            ?? throw new KeyNotFoundException($"Journal template {request.TemplateId} not found.");

        template.DeletedAt = clock.UtcNow;
        await db.SaveChangesAsync(ct);
    }
}
