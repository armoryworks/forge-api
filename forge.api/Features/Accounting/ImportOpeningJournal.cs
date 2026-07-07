using System.Security.Claims;

using FluentValidation;
using MediatR;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;

using Forge.Api.Capabilities;
using Forge.Core.Enums.Accounting;
using Forge.Core.Interfaces;
using Forge.Core.Models.Accounting;
using Forge.Data.Context;

namespace Forge.Api.Features.Accounting;

/// <summary>
/// §7A conversion workstream — posts a book's <b>opening journal</b> (one balanced
/// <see cref="JournalSource.Conversion"/> entry) from the cutover CSV template
/// (columns: <c>accountNumber, debit, credit, description?</c> — parsed by the caller into lines).
/// <para>
/// Gated behind <c>CAP-ACCT-MIGRATION</c>, NOT <c>CAP-ACCT-FULLGL</c> — the opening journal is the
/// <em>prerequisite</em> the FULLGL enable-gate checks (chicken-and-egg otherwise). Idempotent per book
/// (<c>Conversion:Book:{id}:OPENING</c>): re-running the import returns the already-posted entry.
/// PS-run flow (decision 2026-07-07): enable MIGRATION → import → tie out the opening TB against the
/// legacy closing TB (see <c>GetConversionTieOutQuery</c>) → enable FULLGL.
/// </para>
/// </summary>
[RequiresCapability("CAP-ACCT-MIGRATION")]
public record ImportOpeningJournalCommand(
    int BookId,
    DateOnly AsOfDate,
    int CurrencyId,
    IReadOnlyList<OpeningJournalLineModel> Lines) : IRequest<ManualJournalEntryResult>;

/// <summary>One CSV row: the account (by number, per the template) and exactly one of debit/credit.</summary>
public record OpeningJournalLineModel(string AccountNumber, decimal Debit, decimal Credit, string? Description = null);

public class ImportOpeningJournalValidator : AbstractValidator<ImportOpeningJournalCommand>
{
    public ImportOpeningJournalValidator()
    {
        RuleFor(x => x.BookId).GreaterThan(0);
        RuleFor(x => x.CurrencyId).GreaterThan(0);
        RuleFor(x => x.Lines)
            .Must(lines => lines is { Count: >= 2 })
            .WithMessage("An opening journal requires at least two lines.");
        RuleFor(x => x.Lines)
            .Must(lines => lines is null || Math.Abs(lines.Sum(l => l.Debit) - lines.Sum(l => l.Credit)) == 0m)
            .WithMessage("The opening journal must balance: total debits must equal total credits (native opening TB == legacy closing TB, §7A).");

        RuleForEach(x => x.Lines).ChildRules(line =>
        {
            line.RuleFor(l => l.AccountNumber).NotEmpty().MaximumLength(50);
            line.RuleFor(l => l).Must(l => (l.Debit == 0m) != (l.Credit == 0m))
                .WithMessage("Exactly one of debit/credit must be non-zero on each line.");
            line.RuleFor(l => l.Debit).GreaterThanOrEqualTo(0m);
            line.RuleFor(l => l.Credit).GreaterThanOrEqualTo(0m);
        });
    }
}

public class ImportOpeningJournalHandler(
    AppDbContext db,
    IPostingEngine postingEngine,
    IHttpContextAccessor httpContextAccessor)
    : IRequestHandler<ImportOpeningJournalCommand, ManualJournalEntryResult>
{
    public async Task<ManualJournalEntryResult> Handle(ImportOpeningJournalCommand request, CancellationToken cancellationToken)
    {
        // Server-trusted principal (§5.2/§5.7) — the engine records it as PostedBy.
        var postedByUserId = int.Parse(
            httpContextAccessor.HttpContext?.User.FindFirstValue(ClaimTypes.NameIdentifier) ?? "0");

        // Resolve the template's account numbers to this book's accounts; name every miss at once.
        var numbers = request.Lines.Select(l => l.AccountNumber.Trim()).Distinct().ToList();
        var accountIdByNumber = await db.GlAccounts
            .AsNoTracking()
            .IgnoreQueryFilters()
            .Where(a => a.BookId == request.BookId && numbers.Contains(a.AccountNumber))
            .ToDictionaryAsync(a => a.AccountNumber, a => a.Id, cancellationToken);
        var unknown = numbers.Where(n => !accountIdByNumber.ContainsKey(n)).ToList();
        if (unknown.Count > 0)
        {
            throw new PostingException(
                "CONVERSION_UNKNOWN_ACCOUNTS",
                $"Book {request.BookId} has no account(s) numbered: {string.Join(", ", unknown)}. " +
                "Create them in the chart of accounts (or fix the CSV) before importing.");
        }

        var entry = await postingEngine.PostAsync(
            new PostingRequest
            {
                BookId = request.BookId,
                EntryDate = request.AsOfDate,
                Source = JournalSource.Conversion,
                SourceType = "OpeningJournal",
                CurrencyId = request.CurrencyId,
                Memo = $"Opening balances as of {request.AsOfDate:yyyy-MM-dd} (§7A conversion)",
                // Non-Manual sources REQUIRE a key (§5.1); one key per book makes the import idempotent.
                IdempotencyKey = $"Conversion:Book:{request.BookId}:OPENING",
                Lines = request.Lines.Select(l => new PostingLine
                {
                    GlAccountId = accountIdByNumber[l.AccountNumber.Trim()],
                    Debit = l.Debit,
                    Credit = l.Credit,
                    Description = l.Description,
                }).ToList(),
            },
            postedByUserId,
            cancellationToken);

        return entry.ToManualResult();
    }
}

/// <summary>
/// §7A go-live tie-out read: the book's native trial balance, reachable under <c>CAP-ACCT-MIGRATION</c>
/// (FULLGL is still OFF at tie-out time — that's the point). PS compares this against the legacy
/// system's closing TB before flipping FULLGL on.
/// </summary>
[RequiresCapability("CAP-ACCT-MIGRATION")]
public record GetConversionTieOutQuery(int BookId, DateOnly? AsOfDate = null) : IRequest<TrialBalance>;

public class GetConversionTieOutHandler(ITrialBalanceService trialBalanceService)
    : IRequestHandler<GetConversionTieOutQuery, TrialBalance>
{
    public Task<TrialBalance> Handle(GetConversionTieOutQuery request, CancellationToken cancellationToken)
        => trialBalanceService.GetTrialBalanceAsync(request.BookId, null, request.AsOfDate, cancellationToken);
}
