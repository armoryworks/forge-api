using System.Security.Claims;

using FluentValidation;

using MediatR;

using Microsoft.EntityFrameworkCore;

using Forge.Core.Entities;
using Forge.Core.Models;
using Forge.Data.Context;

namespace Forge.Api.Features.Expenses;

public record CreateRecurringExpenseCommand(CreateRecurringExpenseRequestModel Request) : IRequest<RecurringExpenseResponseModel>;

public class CreateRecurringExpenseValidator : AbstractValidator<CreateRecurringExpenseCommand>
{
    public CreateRecurringExpenseValidator()
    {
        RuleFor(x => x.Request.Amount).GreaterThan(0);
        RuleFor(x => x.Request.Category).NotEmpty().MaximumLength(100);
        RuleFor(x => x.Request.Classification).NotEmpty().MaximumLength(100);
        RuleFor(x => x.Request.Description).NotEmpty().MaximumLength(1000);
        RuleFor(x => x.Request.Vendor).MaximumLength(200);
    }
}

public class CreateRecurringExpenseHandler(
    AppDbContext db,
    IHttpContextAccessor httpContextAccessor) : IRequestHandler<CreateRecurringExpenseCommand, RecurringExpenseResponseModel>
{
    public async Task<RecurringExpenseResponseModel> Handle(CreateRecurringExpenseCommand request, CancellationToken cancellationToken)
    {
        var userId = int.Parse(httpContextAccessor.HttpContext!.User.FindFirstValue(ClaimTypes.NameIdentifier)!);
        var user = await db.Users.AsNoTracking().FirstAsync(u => u.Id == userId, cancellationToken);

        var entity = new RecurringExpense
        {
            UserId = userId,
            Amount = request.Request.Amount,
            Category = request.Request.Category,
            Classification = request.Request.Classification,
            Description = request.Request.Description,
            Vendor = request.Request.Vendor,
            Frequency = request.Request.Frequency,
            NextOccurrenceDate = request.Request.StartDate,
            EndDate = request.Request.EndDate,
            AutoApprove = request.Request.AutoApprove,
            IsActive = true,
        };

        db.RecurringExpenses.Add(entity);
        await db.SaveChangesAsync(cancellationToken);

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
