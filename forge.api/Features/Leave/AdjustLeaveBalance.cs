using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;

using Forge.Core.Entities;
using Forge.Core.Enums;
using Forge.Core.Models;
using Forge.Data.Context;
using Forge.Data.Extensions;

namespace Forge.Api.Features.Leave;

public record AdjustLeaveBalanceCommand(int UserId, AdjustLeaveBalanceRequestModel Request) : IRequest<LeaveBalanceAdjustmentResponseModel>;

public class AdjustLeaveBalanceValidator : AbstractValidator<AdjustLeaveBalanceCommand>
{
    public AdjustLeaveBalanceValidator()
    {
        RuleFor(x => x.UserId).GreaterThan(0);
        RuleFor(x => x.Request.PolicyId).GreaterThan(0);
        RuleFor(x => x.Request.Type).IsInEnum();
        RuleFor(x => x.Request.EffectiveDate).NotEmpty();
        RuleFor(x => x.Request.Hours).InclusiveBetween(-9999m, 9999m);
        RuleFor(x => x.Request.Hours).GreaterThanOrEqualTo(0)
            .When(x => x.Request.Type == LeaveAdjustmentType.Opening)
            .WithMessage("An opening balance cannot be negative.");
        RuleFor(x => x.Request.Hours).NotEqual(0)
            .When(x => x.Request.Type == LeaveAdjustmentType.Adjustment)
            .WithMessage("An adjustment must add or remove some hours.");
        RuleFor(x => x.Request.Reason).NotEmpty()
            .When(x => x.Request.Type == LeaveAdjustmentType.Adjustment)
            .WithMessage("Give a reason for the adjustment.");
        RuleFor(x => x.Request.Reason).MaximumLength(500);
    }
}

public class AdjustLeaveBalanceHandler(AppDbContext db)
    : IRequestHandler<AdjustLeaveBalanceCommand, LeaveBalanceAdjustmentResponseModel>
{
    public async Task<LeaveBalanceAdjustmentResponseModel> Handle(AdjustLeaveBalanceCommand request, CancellationToken cancellationToken)
    {
        var data = request.Request;
        var policy = await db.LeavePolicies.AsNoTracking().FirstOrDefaultAsync(p => p.Id == data.PolicyId, cancellationToken)
            ?? throw new KeyNotFoundException($"Leave policy {data.PolicyId} not found");
        var user = await db.Users.AsNoTracking().FirstOrDefaultAsync(u => u.Id == request.UserId, cancellationToken)
            ?? throw new KeyNotFoundException($"User {request.UserId} not found");

        await using var tx = db.Database.CurrentTransaction is null
            ? await db.Database.BeginTransactionAsync(cancellationToken)
            : null;
        await db.AcquireAdvisoryLockAsync("leave-balance", $"{request.UserId}:{policy.Id}", cancellationToken);

        if (data.Type == LeaveAdjustmentType.Opening
            && await db.LeaveBalanceAdjustments.AnyAsync(a => a.UserId == request.UserId && a.PolicyId == policy.Id
                && a.AdjustmentType == LeaveAdjustmentType.Opening, cancellationToken))
            throw new InvalidOperationException(
                $"{user.LastName}, {user.FirstName} already has an opening {policy.Name} balance. Record an adjustment instead.");

        var balance = await db.LeaveBalances
            .FirstOrDefaultAsync(b => b.UserId == request.UserId && b.PolicyId == policy.Id, cancellationToken);
        if (balance is null)
        {
            balance = new LeaveBalance
            {
                UserId = request.UserId,
                PolicyId = policy.Id,
                LastAccrualDate = new DateTimeOffset(data.EffectiveDate.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero),
            };
            db.LeaveBalances.Add(balance);
        }

        var after = balance.Balance + data.Hours;
        if (after < 0)
            throw new InvalidOperationException(
                $"This adjustment would leave {policy.Name} at {after:0.##} h. The balance cannot go below zero.");
        balance.Balance = after;

        var adjustment = new LeaveBalanceAdjustment
        {
            UserId = request.UserId,
            PolicyId = policy.Id,
            AdjustmentType = data.Type,
            Hours = data.Hours,
            EffectiveDate = data.EffectiveDate,
            Reason = string.IsNullOrWhiteSpace(data.Reason) ? null : data.Reason.Trim(),
            BalanceAfter = after,
            CreatedByUserId = db.CurrentUserId,
        };
        db.LeaveBalanceAdjustments.Add(adjustment);

        var verb = data.Type == LeaveAdjustmentType.Opening ? "Opening" : "Adjusted";
        db.LogActivityAt(
            data.Type == LeaveAdjustmentType.Opening ? "leave-opening-balance" : "leave-balance-adjusted",
            $"{verb} {policy.Name} balance: {data.Hours:+0.##;-0.##;0} h, now {after:0.##} h (as of {data.EffectiveDate:yyyy-MM-dd})",
            ("ApplicationUser", request.UserId));

        await db.SaveChangesAsync(cancellationToken);
        if (tx is not null)
            await tx.CommitAsync(cancellationToken);

        var createdByName = adjustment.CreatedByUserId is int by
            ? await db.Users.AsNoTracking().Where(u => u.Id == by)
                .Select(u => u.LastName + ", " + u.FirstName).FirstOrDefaultAsync(cancellationToken)
            : null;

        return new LeaveBalanceAdjustmentResponseModel(
            adjustment.Id, adjustment.UserId, adjustment.PolicyId, policy.Name, adjustment.AdjustmentType,
            adjustment.Hours, adjustment.EffectiveDate, adjustment.Reason, adjustment.BalanceAfter,
            adjustment.CreatedByUserId, createdByName, adjustment.CreatedAt);
    }
}
