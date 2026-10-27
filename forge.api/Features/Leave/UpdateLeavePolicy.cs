using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;

using Forge.Core.Models;
using Forge.Data.Context;
using Forge.Data.Extensions;

namespace Forge.Api.Features.Leave;

public record UpdateLeavePolicyCommand(int Id, UpdateLeavePolicyRequestModel Request) : IRequest<LeavePolicyResponseModel>;

public class UpdateLeavePolicyValidator : AbstractValidator<UpdateLeavePolicyCommand>
{
    public UpdateLeavePolicyValidator()
    {
        RuleFor(x => x.Request.Name).NotEmpty().MaximumLength(100);
        RuleFor(x => x.Request.AccrualRatePerPayPeriod).GreaterThanOrEqualTo(0);
        RuleFor(x => x.Request.MaxBalance).GreaterThanOrEqualTo(0).When(x => x.Request.MaxBalance.HasValue);
        RuleFor(x => x.Request.CarryOverLimit).GreaterThanOrEqualTo(0).When(x => x.Request.CarryOverLimit.HasValue);
        RuleFor(x => x.Request.WaitingPeriodDays).GreaterThanOrEqualTo(0).When(x => x.Request.WaitingPeriodDays.HasValue);
    }
}

public class UpdateLeavePolicyHandler(AppDbContext db) : IRequestHandler<UpdateLeavePolicyCommand, LeavePolicyResponseModel>
{
    public async Task<LeavePolicyResponseModel> Handle(UpdateLeavePolicyCommand request, CancellationToken cancellationToken)
    {
        var policy = await db.LeavePolicies.FirstOrDefaultAsync(p => p.Id == request.Id, cancellationToken)
            ?? throw new KeyNotFoundException($"Leave policy {request.Id} not found");

        var name = request.Request.Name.Trim();
        if (await db.LeavePolicies.AnyAsync(p => p.Id != policy.Id && p.Name == name, cancellationToken))
            throw new InvalidOperationException($"Another leave policy is already named '{name}'.");

        policy.Name = name;
        policy.AccrualRatePerPayPeriod = request.Request.AccrualRatePerPayPeriod;
        policy.MaxBalance = request.Request.MaxBalance;
        policy.CarryOverLimit = request.Request.CarryOverLimit;
        policy.AccrueFromHireDate = request.Request.AccrueFromHireDate;
        policy.WaitingPeriodDays = request.Request.WaitingPeriodDays;
        policy.IsPaidLeave = request.Request.IsPaidLeave;
        policy.IsActive = request.Request.IsActive;

        db.ChangeTracker.DetectChanges();
        if (ActivityChangeSummary.Updated(ActivityChangeSummary.ChangedProperties(db.Entry(policy))) is string summary)
            db.LogActivityAt("updated", summary, ("LeavePolicy", policy.Id));

        await db.SaveChangesAsync(cancellationToken);

        return new LeavePolicyResponseModel(
            policy.Id, policy.Name, policy.AccrualRatePerPayPeriod,
            policy.MaxBalance, policy.CarryOverLimit,
            policy.AccrueFromHireDate, policy.WaitingPeriodDays,
            policy.IsPaidLeave, policy.IsActive);
    }
}
