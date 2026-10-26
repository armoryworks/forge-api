using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;

using Forge.Api.Services;
using Forge.Api.Validation;
using Forge.Core.Entities;
using Forge.Core.Enums;
using Forge.Core.Interfaces;
using Forge.Core.Models;
using Forge.Data.Context;
using Forge.Data.Extensions;

namespace Forge.Api.Features.Quotes;

/// <summary>
/// Copy a quote into a new Draft. With <paramref name="AsRevision"/> the copy is the next revision of
/// a Sent, Declined or Expired quote: it is numbered after the source (<c>Q-1001-R1</c>, then
/// <c>-R2</c>; the next sequence number when that does not fit or is taken), keeps the customer PO,
/// and the source becomes <see cref="QuoteStatus.Superseded"/> so only the latest revision can be
/// accepted. The link to the estimate the quote came from moves to the revision, so the estimate
/// keeps pointing at its current quote. Both quotes record the link in their activity history.
/// </summary>
public record DuplicateQuoteCommand(int Id, bool AsRevision = false) : IRequest<QuoteDetailResponseModel>;

public class DuplicateQuoteValidator : AbstractValidator<DuplicateQuoteCommand>
{
    public DuplicateQuoteValidator()
    {
        RuleFor(x => x.Id).GreaterThan(0);
    }
}

public class DuplicateQuoteHandler(
    AppDbContext db,
    IQuoteRepository repo,
    IMediator mediator,
    TaxOverrideGuard taxGuard,
    IBusinessIdentifierService identifiers)
    : IRequestHandler<DuplicateQuoteCommand, QuoteDetailResponseModel>
{
    public async Task<QuoteDetailResponseModel> Handle(DuplicateQuoteCommand request, CancellationToken cancellationToken)
    {
        var source = await db.Quotes
            .AsNoTracking()
            .Include(q => q.Customer)
            .Include(q => q.Lines)
            .FirstOrDefaultAsync(q => q.Id == request.Id && q.Type == QuoteType.Quote, cancellationToken)
            ?? throw new KeyNotFoundException($"Quote {request.Id} not found");

        if (request.AsRevision && !RevisableStatuses.Contains(source.Status))
            throw new InvalidOperationException("Only Sent, Declined or Expired quotes can be revised.");

        ActiveCheck.EnsureActive(source.Customer, "Customer", "customerId", source.CustomerId);

        var lines = source.Lines.OrderBy(l => l.LineNumber).ToList();
        for (var i = 0; i < lines.Count; i++)
        {
            if (lines[i].PartId is not int partId) continue;
            var part = await db.Parts.FindAsync([partId], cancellationToken);
            ActiveCheck.EnsureActive(part, "Part", $"lines[{i}].partId", partId);
        }

        var defaultRate = await taxGuard.GetDefaultRateAsync(source.CustomerId, cancellationToken);
        var taxDocumentId = await taxGuard.EnsureCanOverrideAsync(
            source.CustomerId, source.TaxRate, defaultRate, cancellationToken);

        var copy = new Quote
        {
            Type = QuoteType.Quote,
            Status = QuoteStatus.Draft,
            QuoteNumber = request.AsRevision
                ? await RevisionNumberAsync(source.QuoteNumber, cancellationToken)
                : await repo.GenerateNextQuoteNumberAsync(cancellationToken),
            CustomerId = source.CustomerId,
            ShippingAddressId = source.ShippingAddressId,
            Notes = source.Notes,
            TaxRate = source.TaxRate,
            TaxDocumentId = taxDocumentId,
            CustomerPO = request.AsRevision ? source.CustomerPO : null,
            AssignedToId = request.AsRevision ? source.AssignedToId ?? db.CurrentUserId : db.CurrentUserId,
        };

        var lineNumber = 1;
        foreach (var line in lines)
        {
            copy.Lines.Add(new QuoteLine
            {
                PartId = line.PartId,
                Description = line.Description,
                Quantity = line.Quantity,
                UnitPrice = line.UnitPrice,
                LineNumber = lineNumber++,
                Notes = line.Notes,
            });
        }

        await using var tx = await db.Database.BeginTransactionAsync(cancellationToken);
        if (request.AsRevision)
        {
            var superseded = await db.Quotes.FirstAsync(q => q.Id == source.Id, cancellationToken);
            superseded.Status = QuoteStatus.Superseded;
            superseded.SourceEstimateId = null;
            await db.SaveChangesAsync(cancellationToken);
            copy.SourceEstimateId = source.SourceEstimateId;
        }

        db.Quotes.Add(copy);
        await CopyPaymentScheduleAsync(source.Id, copy, cancellationToken);
        await db.SaveChangesAsync(cancellationToken);

        if (request.AsRevision)
        {
            db.LogActivityAt("created", $"Created (revision of {source.QuoteNumber})", ("Quote", copy.Id));
            db.LogActivityAt("superseded", $"Superseded by revision {copy.QuoteNumber}", ("Quote", source.Id));
        }
        else
        {
            db.LogActivityAt("created", $"Created (duplicated from {source.QuoteNumber})", ("Quote", copy.Id));
        }
        await identifiers.IssueAsync(BusinessEntityType.Quote, copy.Id, copy.QuoteNumber!, cancellationToken);
        await db.SaveChangesAsync(cancellationToken);
        await tx.CommitAsync(cancellationToken);

        return await mediator.Send(new GetQuoteByIdQuery(copy.Id), cancellationToken);
    }

    private static readonly QuoteStatus[] RevisableStatuses = [QuoteStatus.Sent, QuoteStatus.Declined, QuoteStatus.Expired];
    private const int QuoteNumberMaxLength = 20;

    private async Task<string> RevisionNumberAsync(string? sourceNumber, CancellationToken ct)
    {
        if (!string.IsNullOrWhiteSpace(sourceNumber))
        {
            var match = System.Text.RegularExpressions.Regex.Match(sourceNumber, @"^(?<base>.+?)-R(?<rev>\d+)$");
            var baseNumber = match.Success ? match.Groups["base"].Value : sourceNumber;
            var revision = match.Success && int.TryParse(match.Groups["rev"].Value, out var r) ? r + 1 : 1;
            var candidate = $"{baseNumber}-R{revision}";

            var taken = await db.Quotes.IgnoreQueryFilters().AnyAsync(q => q.QuoteNumber == candidate, ct);
            if (candidate.Length <= QuoteNumberMaxLength && !taken)
                return candidate;
        }

        return await repo.GenerateNextQuoteNumberAsync(ct);
    }

    private async Task CopyPaymentScheduleAsync(int sourceQuoteId, Quote copy, CancellationToken ct)
    {
        var schedule = await db.PaymentSchedules
            .AsNoTracking()
            .Include(s => s.Milestones)
            .Where(s => s.QuoteId == sourceQuoteId && s.Status != PaymentScheduleStatus.Cancelled)
            .OrderByDescending(s => s.Id)
            .FirstOrDefaultAsync(ct);
        if (schedule is null || schedule.Milestones.Count == 0) return;

        var duplicate = new PaymentSchedule { Quote = copy, Status = PaymentScheduleStatus.Draft };
        foreach (var milestone in schedule.Milestones.OrderBy(m => m.Sequence))
        {
            duplicate.Milestones.Add(new PaymentMilestone
            {
                Sequence = milestone.Sequence,
                Name = milestone.Name,
                Percentage = milestone.Percentage,
                DueTrigger = milestone.DueTrigger,
                DueDate = milestone.DueDate,
                NetDays = milestone.NetDays,
                Status = PaymentMilestoneStatus.Pending,
                Notes = milestone.Notes,
            });
        }
        db.PaymentSchedules.Add(duplicate);
    }
}
