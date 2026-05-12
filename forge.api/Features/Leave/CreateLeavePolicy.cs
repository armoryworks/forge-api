using FluentValidation;
using MediatR;

using Forge.Core.Entities;
using Forge.Core.Models;
using Forge.Data.Context;

namespace Forge.Api.Features.Leave;

public record CreateLeavePolicyCommand(CreateLeavePolicyRequestModel Request) : IRequest<LeavePolicyResponseModel>;

public class CreateLeavePolicyValidator : AbstractValidator<CreateLeavePolicyCommand>
{
    public CreateLeavePolicyValidator()
    {
        RuleFor(x => x.Request.Name).NotEmpty().MaximumLength(100);
        RuleFor(x => x.Request.AccrualRatePerPayPeriod).GreaterThanOrEqualTo(0);
    }
}

public class CreateLeavePolicyHandler(AppDbContext db) : IRequestHandler<CreateLeavePolicyCommand, LeavePolicyResponseModel>
{
    public async Task<LeavePolicyResponseModel> Handle(CreateLeavePolicyCommand request, CancellationToken cancellationToken)
    {
        var policy = new LeavePolicy
        {
            Name = request.Request.Name.Trim(),
            AccrualRatePerPayPeriod = request.Request.AccrualRatePerPayPeriod,
            MaxBalance = request.Request.MaxBalance,
            CarryOverLimit = request.Request.CarryOverLimit,
            AccrueFromHireDate = request.Request.AccrueFromHireDate,
            WaitingPeriodDays = request.Request.WaitingPeriodDays,
            IsPaidLeave = request.Request.IsPaidLeave,
        };

        db.LeavePolicies.Add(policy);
        await db.SaveChangesAsync(cancellationToken);

        return new LeavePolicyResponseModel(
            policy.Id, policy.Name, policy.AccrualRatePerPayPeriod,
            policy.MaxBalance, policy.CarryOverLimit,
            policy.AccrueFromHireDate, policy.WaitingPeriodDays,
            policy.IsPaidLeave, policy.IsActive);
    }
}
