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

public record DuplicateQuoteCommand(int Id) : IRequest<QuoteDetailResponseModel>;

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
            QuoteNumber = await repo.GenerateNextQuoteNumberAsync(cancellationToken),
            CustomerId = source.CustomerId,
            ShippingAddressId = source.ShippingAddressId,
            Notes = source.Notes,
            TaxRate = source.TaxRate,
            TaxDocumentId = taxDocumentId,
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

        db.Quotes.Add(copy);
        await CopyPaymentScheduleAsync(source.Id, copy, cancellationToken);
        await db.SaveChangesAsync(cancellationToken);

        db.LogActivityAt("created", $"Created (duplicated from {source.QuoteNumber})", ("Quote", copy.Id));
        await identifiers.IssueAsync(BusinessEntityType.Quote, copy.Id, copy.QuoteNumber!, cancellationToken);
        await db.SaveChangesAsync(cancellationToken);

        return await mediator.Send(new GetQuoteByIdQuery(copy.Id), cancellationToken);
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
