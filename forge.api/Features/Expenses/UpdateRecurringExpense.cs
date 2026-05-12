using FluentValidation;

using MediatR;

using Microsoft.EntityFrameworkCore;

using Forge.Core.Models;
using Forge.Data.Context;

namespace Forge.Api.Features.Expenses;

public record UpdateRecurringExpenseCommand(int Id, UpdateRecurringExpenseRequestModel Request) : IRequest<RecurringExpenseResponseModel>;

public class UpdateRecurringExpenseValidator : AbstractValidator<UpdateRecurringExpenseCommand>
{
    public UpdateRecurringExpenseValidator()
    {
        RuleFor(x => x.Id).GreaterThan(0);
        RuleFor(x => x.Request.Amount).GreaterThan(0).When(x => x.Request.Amount.HasValue);
        RuleFor(x => x.Request.Category).MaximumLength(100).When(x => x.Request.Category != null);
        RuleFor(x => x.Request.Classification).MaximumLength(100).When(x => x.Request.Classification != null);
        RuleFor(x => x.Request.Description).MaximumLength(1000).When(x => x.Request.Description != null);
        RuleFor(x => x.Request.Vendor).MaximumLength(200).When(x => x.Request.Vendor != null);
    }
}

public class UpdateRecurringExpenseHandler(AppDbContext db) : IRequestHandler<UpdateRecurringExpenseCommand, RecurringExpenseResponseModel>
{
    public async Task<RecurringExpenseResponseModel> Handle(UpdateRecurringExpenseCommand request, CancellationToken cancellationToken)
    {
        var entity = await db.RecurringExpenses.FindAsync([request.Id], cancellationToken)
            ?? throw new KeyNotFoundException($"Recurring expense {request.Id} not found");

        var r = request.Request;
        if (r.Amount.HasValue) entity.Amount = r.Amount.Value;
        if (r.Category != null) entity.Category = r.Category;
        if (r.Classification != null) entity.Classification = r.Classification;
        if (r.Description != null) entity.Description = r.Description;
        if (r.Vendor != null) entity.Vendor = r.Vendor;
        if (r.Frequency.HasValue) entity.Frequency = r.Frequency.Value;
        if (r.EndDate.HasValue) entity.EndDate = r.EndDate.Value;
        if (r.IsActive.HasValue) entity.IsActive = r.IsActive.Value;
        if (r.AutoApprove.HasValue) entity.AutoApprove = r.AutoApprove.Value;

        await db.SaveChangesAsync(cancellationToken);

        var user = await db.Users.AsNoTracking().FirstAsync(u => u.Id == entity.UserId, cancellationToken);

        return new RecurringExpenseResponseModel(
            entity.Id,
            entity.UserId,
            $"{user.FirstName} {user.LastName}".Trim(),
            entity.Amount,
            entity.Category,
            entity.Classification,
            entity.Description,
            entity.Vendor,
            entity.Frequency,
            entity.NextOccurrenceDate,
            entity.LastGeneratedDate,
            entity.EndDate,
            entity.IsActive,
            entity.AutoApprove,
            entity.CreatedAt
        );
    }
}
