using Microsoft.EntityFrameworkCore;

using Forge.Core.Entities.Accounting;
using Forge.Core.Interfaces;
using Forge.Core.Models.Accounting;
using Forge.Data.Context;

namespace Forge.Api.Features.Accounting;

/// <inheritdoc />
public sealed class JournalTemplateService(AppDbContext db, IPostingEngine postingEngine) : IJournalTemplateService
{
    public async Task<JournalTemplateModel> CreateAsync(CreateJournalTemplateModel model, CancellationToken ct = default)
    {
        if (model.Lines is not { Count: > 0 })
            throw new InvalidOperationException("A journal template needs at least one line.");

        var lineNumber = 1;
        var template = new JournalTemplate
        {
            BookId = model.BookId,
            Name = model.Name,
            Description = model.Description,
            Memo = model.Memo,
            AutoReverseNextPeriod = model.AutoReverseNextPeriod,
            IsActive = true,
            Lines = model.Lines.Select(l => new JournalTemplateLine
            {
                LineNumber = lineNumber++,
                AccountDeterminationKey = l.AccountDeterminationKey,
                GlAccountId = l.GlAccountId,
                Debit = l.Debit,
                Credit = l.Credit,
                Description = l.Description,
                PartyType = l.PartyType,
                PartyId = l.PartyId,
            }).ToList(),
        };

        db.JournalTemplates.Add(template);
        await db.SaveChangesAsync(ct);

        return Map(template);
    }

    public async Task<IReadOnlyList<JournalTemplateModel>> ListAsync(int bookId, CancellationToken ct = default)
    {
        var templates = await db.JournalTemplates
            .AsNoTracking()
            .Where(t => t.BookId == bookId)
            .Include(t => t.Lines)
            .OrderBy(t => t.Name)
            .ToListAsync(ct);
        return templates.Select(Map).ToList();
    }

    public async Task<JournalTemplateModel?> GetAsync(int templateId, CancellationToken ct = default)
    {
        var template = await db.JournalTemplates
            .AsNoTracking()
            .Include(t => t.Lines)
            .FirstOrDefaultAsync(t => t.Id == templateId, ct);
        return template is null ? null : Map(template);
    }

    public async Task<PostedFromTemplateModel> PostFromTemplateAsync(
        int templateId, DateOnly entryDate, string? memoOverride, int postedByUserId, CancellationToken ct = default)
    {
        var template = await db.JournalTemplates
            .Include(t => t.Lines)
            .FirstOrDefaultAsync(t => t.Id == templateId, ct)
            ?? throw new KeyNotFoundException($"Journal template {templateId} not found.");

        if (!template.IsActive)
            throw new InvalidOperationException($"Journal template {template.Name} is inactive.");

        var currencyId = await db.Books.AsNoTracking()
            .Where(b => b.Id == template.BookId)
            .Select(b => (int?)b.FunctionalCurrencyId)
            .FirstOrDefaultAsync(ct)
            ?? throw new PostingException("NO_POSTING_BOOK", $"Book {template.BookId} not found for the template.");

        var request = new PostingRequest
        {
            BookId = template.BookId,
            EntryDate = entryDate,
            Source = template.Source,
            SourceType = "JournalTemplate",
            SourceId = template.Id,
            CurrencyId = currencyId,
            Memo = memoOverride ?? template.Memo ?? $"Template: {template.Name}",
            // Idempotent per (template, date): re-posting the same template on the same date returns the entry.
            IdempotencyKey = $"{template.Source}:Template:{template.Id}:{entryDate:yyyyMMdd}",
            AutoReverseNextPeriod = template.AutoReverseNextPeriod,
            Lines = template.Lines
                .OrderBy(l => l.LineNumber)
                .Select(l => new PostingLine
                {
                    AccountKey = l.AccountDeterminationKey,
                    GlAccountId = l.GlAccountId,
                    Debit = l.Debit,
                    Credit = l.Credit,
                    Description = l.Description,
                    PartyType = l.PartyType,
                    PartyId = l.PartyId,
                })
                .ToList(),
        };

        var entry = await postingEngine.PostAsync(request, postedByUserId, ct);
        return new PostedFromTemplateModel(entry.Id, entry.EntryNumber, entry.EntryDate);
    }

    private static JournalTemplateModel Map(JournalTemplate t) => new(
        t.Id, t.BookId, t.Name, t.Description, t.Memo, t.AutoReverseNextPeriod, t.IsActive,
        t.Lines.OrderBy(l => l.LineNumber).Select(l => new JournalTemplateLineModel(
            l.AccountDeterminationKey, l.GlAccountId, l.Debit, l.Credit, l.Description, l.PartyType, l.PartyId)).ToList());
}
