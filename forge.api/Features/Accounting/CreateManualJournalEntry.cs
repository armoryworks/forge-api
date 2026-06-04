using System.Security.Claims;

using FluentValidation;
using MediatR;
using Microsoft.AspNetCore.Http;

using Forge.Api.Capabilities;
using Forge.Core.Enums.Accounting;
using Forge.Core.Interfaces;
using Forge.Core.Models.Accounting;

namespace Forge.Api.Features.Accounting;

/// <summary>
/// Phase-0 manual journal-entry API surface (§5.5 / §5.9 acceptance: "post a
/// manual balanced journal"). Mirrors the codebase's MediatR command/handler
/// feature pattern (cf. <c>Features/PurchaseOrders/CreatePurchaseOrder</c>):
/// the handler builds a <see cref="PostingRequest"/> and calls the single GL
/// write seam <see cref="IPostingEngine.PostAsync"/> — it never touches
/// <c>JournalEntry</c> directly (§5.2, §7).
/// <para>
/// <b>DARK in Phase 0.</b> The command type carries
/// <see cref="RequiresCapabilityAttribute"/> for <c>CAP-ACCT-FULLGL</c>; with
/// that capability OFF the controller-edge <c>CapabilityGateMiddleware</c> AND
/// the MediatR <c>CapabilityGateBehavior</c> both short-circuit the request, so
/// the engine is unreachable. No operational command site calls this — it is
/// reachable only via the gated <c>/api/v1/accounting/journal-entries</c>
/// endpoint and the handler tests.
/// </para>
/// </summary>
[RequiresCapability("CAP-ACCT-FULLGL")]
public record CreateManualJournalEntryCommand(
    int BookId,
    DateOnly EntryDate,
    int CurrencyId,
    string? Memo,
    bool AllowSoftClosedOverride,
    IReadOnlyList<CreateManualJournalLineModel> Lines)
    : IRequest<ManualJournalEntryResult>;

/// <summary>
/// One debit/credit line of a manual JE request. Exactly one of
/// <see cref="Debit"/>/<see cref="Credit"/> is non-zero and the account is given
/// by an explicit <see cref="GlAccountId"/> XOR a determination
/// <see cref="AccountKey"/> — the engine re-validates both invariants (§5.2).
/// </summary>
public record CreateManualJournalLineModel(
    int? GlAccountId,
    string? AccountKey,
    int? JobId,
    int? CostCenterId,
    SubledgerPartyType? PartyType,
    int? PartyId,
    decimal Debit,
    decimal Credit,
    string? Description);

/// <summary>
/// Thin response projection of the posted <c>JournalEntry</c> (we never return
/// the tracked entity itself). Mirrors the §5.9 acceptance shape: proves the
/// entry posted, with its assigned <c>EntryNumber</c> and resolved period.
/// </summary>
public record ManualJournalEntryResult(
    long Id,
    int BookId,
    long EntryNumber,
    DateOnly EntryDate,
    int FiscalPeriodId,
    int FiscalYearId,
    string Status,
    string? Memo,
    int? PostedBy,
    IReadOnlyList<ManualJournalLineResult> Lines);

public record ManualJournalLineResult(
    long Id,
    int LineNumber,
    int GlAccountId,
    decimal Debit,
    decimal Credit,
    decimal FunctionalAmount,
    string? Description);

public class CreateManualJournalEntryValidator : AbstractValidator<CreateManualJournalEntryCommand>
{
    public CreateManualJournalEntryValidator()
    {
        RuleFor(x => x.BookId).GreaterThan(0);
        RuleFor(x => x.CurrencyId).GreaterThan(0);
        RuleFor(x => x.Lines)
            .NotEmpty().WithMessage("At least one line is required")
            .Must(lines => lines == null || lines.Count >= 2)
            .WithMessage("A double-entry journal requires at least two lines");

        RuleForEach(x => x.Lines).ChildRules(line =>
        {
            // Exactly one of (Debit, Credit) is non-zero — rejects 0/0 and
            // both-non-zero. The engine re-validates (DEBIT_CREDIT_XOR) but the
            // 400-at-the-edge keeps the contract honest.
            line.RuleFor(l => l).Must(l => (l.Debit == 0m) != (l.Credit == 0m))
                .WithMessage("Exactly one of debit/credit must be non-zero on each line");
            line.RuleFor(l => l.Debit).GreaterThanOrEqualTo(0m);
            line.RuleFor(l => l.Credit).GreaterThanOrEqualTo(0m);
            // Account is identified by id XOR key.
            line.RuleFor(l => l).Must(l => l.GlAccountId.HasValue ^ !string.IsNullOrWhiteSpace(l.AccountKey))
                .WithMessage("Each line must supply exactly one of glAccountId or accountKey");
        });
    }
}

public class CreateManualJournalEntryHandler(
    IPostingEngine postingEngine,
    IHttpContextAccessor httpContextAccessor)
    : IRequestHandler<CreateManualJournalEntryCommand, ManualJournalEntryResult>
{
    public async Task<ManualJournalEntryResult> Handle(
        CreateManualJournalEntryCommand request, CancellationToken cancellationToken)
    {
        // Server-trusted principal — never client-supplied (§5.2, §5.7). The
        // engine records it as PostedBy.
        var postedByUserId = int.Parse(
            httpContextAccessor.HttpContext?.User.FindFirstValue(ClaimTypes.NameIdentifier) ?? "0");

        // Build the PostingRequest at the command site (the feature pattern):
        // the handler assembles the balanced Dr/Cr request and hands it to the
        // single write seam. Manual source → IdempotencyKey stays null (§5.1).
        var postingRequest = new PostingRequest
        {
            BookId = request.BookId,
            EntryDate = request.EntryDate,
            Source = JournalSource.Manual,
            CurrencyId = request.CurrencyId,
            Memo = request.Memo,
            AllowSoftClosedOverride = request.AllowSoftClosedOverride,
            Lines = request.Lines.Select(l => new PostingLine
            {
                GlAccountId = l.GlAccountId,
                AccountKey = l.AccountKey,
                JobId = l.JobId,
                CostCenterId = l.CostCenterId,
                PartyType = l.PartyType,
                PartyId = l.PartyId,
                Debit = l.Debit,
                Credit = l.Credit,
                Description = l.Description,
            }).ToList(),
        };

        var entry = await postingEngine.PostAsync(postingRequest, postedByUserId, cancellationToken);

        return new ManualJournalEntryResult(
            entry.Id,
            entry.BookId,
            entry.EntryNumber,
            entry.EntryDate,
            entry.FiscalPeriodId,
            entry.FiscalYearId,
            entry.Status.ToString(),
            entry.Memo,
            entry.PostedBy,
            entry.Lines
                .OrderBy(l => l.LineNumber)
                .Select(l => new ManualJournalLineResult(
                    l.Id, l.LineNumber, l.GlAccountId, l.Debit, l.Credit, l.FunctionalAmount, l.Description))
                .ToList());
    }
}
