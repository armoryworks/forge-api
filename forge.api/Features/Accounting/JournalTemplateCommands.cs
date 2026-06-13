using System.Security.Claims;

using MediatR;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;

using Forge.Api.Capabilities;
using Forge.Core.Interfaces;
using Forge.Core.Models.Accounting;
using Forge.Data.Context;

namespace Forge.Api.Features.Accounting;

/// <summary>Phase-3 — create a recurring/standard journal template. CAP-ACCT-FULLGL gated.</summary>
[RequiresCapability("CAP-ACCT-FULLGL")]
public record CreateJournalTemplateCommand(CreateJournalTemplateModel Model) : IRequest<JournalTemplateModel>;

public class CreateJournalTemplateHandler(IJournalTemplateService service)
    : IRequestHandler<CreateJournalTemplateCommand, JournalTemplateModel>
{
    public Task<JournalTemplateModel> Handle(CreateJournalTemplateCommand request, CancellationToken ct)
        => service.CreateAsync(request.Model, ct);
}

/// <summary>Phase-3 — list a book's journal templates. CAP-ACCT-FULLGL gated.</summary>
[RequiresCapability("CAP-ACCT-FULLGL")]
public record ListJournalTemplatesQuery(int BookId) : IRequest<IReadOnlyList<JournalTemplateModel>>;

public class ListJournalTemplatesHandler(IJournalTemplateService service)
    : IRequestHandler<ListJournalTemplatesQuery, IReadOnlyList<JournalTemplateModel>>
{
    public Task<IReadOnlyList<JournalTemplateModel>> Handle(ListJournalTemplatesQuery request, CancellationToken ct)
        => service.ListAsync(request.BookId, ct);
}

/// <summary>Phase-3 — post a journal entry from a template for a given date. CAP-ACCT-FULLGL gated.</summary>
[RequiresCapability("CAP-ACCT-FULLGL")]
public record PostFromTemplateCommand(int TemplateId, DateOnly EntryDate, string? Memo)
    : IRequest<PostedFromTemplateModel>;

public class PostFromTemplateHandler(
    IJournalTemplateService service,
    IHttpContextAccessor? httpContextAccessor = null,
    AppDbContext? db = null)
    : IRequestHandler<PostFromTemplateCommand, PostedFromTemplateModel>
{
    public async Task<PostedFromTemplateModel> Handle(PostFromTemplateCommand request, CancellationToken ct)
    {
        var userId = int.TryParse(
            httpContextAccessor?.HttpContext?.User.FindFirstValue(ClaimTypes.NameIdentifier),
            out var uid) ? uid : 0;

        await using var tx = db is not null ? await db.Database.BeginTransactionAsync(ct) : null;
        var result = await service.PostFromTemplateAsync(request.TemplateId, request.EntryDate, request.Memo, userId, ct);
        if (tx is not null) await tx.CommitAsync(ct);
        return result;
    }
}
