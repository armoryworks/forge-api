using System.Security.Claims;

using FluentValidation;
using MediatR;
using Microsoft.AspNetCore.Http;

using Forge.Api.Capabilities;
using Forge.Api.Middleware;
using Forge.Core.Enums.Accounting;
using Forge.Core.Interfaces;
using Forge.Core.Models.Accounting;
using Forge.Data.Context;

namespace Forge.Api.Features.Accounting;

/// <summary>
/// §7A conversion — posts the opening AR/AP open items as the book's opening-balance entry: each receivable a
/// debit on AR control and each payable a credit on AP control, naming the party the dry run resolves, and the
/// net to Opening Balance Equity. Refused (409) when the dry run finds a problem
/// (<c>opening-open-items-invalid</c>), when the totals do not tie to the legacy control balances
/// (<c>opening-open-items-untied</c>) or when the book's opening-balance entry is already posted
/// (<c>opening-balances-posted</c>). Open under <c>CAP-ACCT-MIGRATION</c> (cutover, FULLGL still off) or
/// <c>CAP-ACCT-FULLGL</c>, like the other conversion loads.
/// </summary>
[RequiresAnyCapability("CAP-ACCT-MIGRATION", "CAP-ACCT-FULLGL")]
public record PostOpeningOpenItemsCommand(PostOpeningOpenItemsRequestModel Model) : IRequest<OpeningBalanceResult>;

public class PostOpeningOpenItemsValidator : AbstractValidator<PostOpeningOpenItemsCommand>
{
    public PostOpeningOpenItemsValidator()
    {
        RuleFor(x => x.Model).NotNull();
        When(x => x.Model is not null, () =>
        {
            RuleFor(x => x.Model.BookId).GreaterThan(0);
            RuleFor(x => x.Model.Rows)
                .NotEmpty()
                .WithMessage("Add at least one open item before posting.")
                .WithErrorCode("opening-open-items-empty");
        });
    }
}

public class PostOpeningOpenItemsHandler(
    AppDbContext db,
    IConversionService conversion,
    IHttpContextAccessor? httpContextAccessor = null)
    : IRequestHandler<PostOpeningOpenItemsCommand, OpeningBalanceResult>
{
    public const string InvalidCode = "opening-open-items-invalid";
    public const string UntiedCode = "opening-open-items-untied";
    public const string AlreadyPostedCode = "opening-balances-posted";
    public const string OffsetKey = "OPENING_BALANCE_EQUITY";
    public const string OffsetDescription = "Opening receivables and payables offset";

    public async Task<OpeningBalanceResult> Handle(PostOpeningOpenItemsCommand request, CancellationToken ct)
    {
        var model = request.Model;
        var check = await new ValidateOpeningOpenItemsHandler(db).Handle(
            new ValidateOpeningOpenItemsQuery(new ValidateOpeningOpenItemsRequestModel(model.BookId, model.Rows)), ct);

        if (check.AlreadyPosted)
            throw new CodedConflictException(AlreadyPostedCode,
                $"Book {model.BookId} already has its opening-balance entry; the open items cannot be posted again.");
        if (check.ErrorCount > 0)
            throw new CodedConflictException(InvalidCode,
                $"{check.Rows.Count(r => r.Errors.Count > 0)} open item(s) have problems; check them and fix them before posting.");
        if (decimal.Round(check.ReceivablesTotal, 2) != decimal.Round(model.ReceivablesControl, 2)
            || decimal.Round(check.PayablesTotal, 2) != decimal.Round(model.PayablesControl, 2))
            throw new CodedConflictException(UntiedCode,
                $"The open items (receivables {check.ReceivablesTotal:0.00}, payables {check.PayablesTotal:0.00}) do not tie to the " +
                $"legacy control balances (receivables {model.ReceivablesControl:0.00}, payables {model.PayablesControl:0.00}).");

        var lines = Lines(model.Rows, check.Rows);
        var userId = int.TryParse(
            httpContextAccessor?.HttpContext?.User.FindFirstValue(ClaimTypes.NameIdentifier), out var uid) ? uid : 0;

        await using var tx = await db.Database.BeginTransactionAsync(ct);
        var result = await conversion.PostOpeningBalancesAsync(
            new PostOpeningBalancesModel(model.BookId, model.AsOfDate, lines), userId, ct);
        await tx.CommitAsync(ct);
        return result;
    }

    internal static List<OpeningBalanceLineModel> Lines(
        IReadOnlyList<OpeningOpenItemRowModel> rows, IReadOnlyList<OpeningOpenItemRowResultModel> resolved)
    {
        var lines = rows.Select((row, i) =>
        {
            var receivable = row.PartyType == SubledgerPartyType.Customer;
            return new OpeningBalanceLineModel(
                receivable ? "AR_CONTROL" : "AP_CONTROL",
                null,
                receivable ? row.Amount : 0m,
                receivable ? 0m : row.Amount,
                row.PartyType,
                resolved[i].PartyId,
                string.IsNullOrWhiteSpace(row.Description) ? null : row.Description.Trim(),
                string.IsNullOrWhiteSpace(row.DocumentNumber) ? null : row.DocumentNumber.Trim(),
                row.DocumentDate,
                row.DueDate);
        }).ToList();

        var net = lines.Sum(l => l.Debit - l.Credit);
        if (net != 0m)
            lines.Add(new OpeningBalanceLineModel(
                OffsetKey, null, net < 0m ? -net : 0m, net > 0m ? net : 0m, null, null, OffsetDescription));
        return lines;
    }
}
