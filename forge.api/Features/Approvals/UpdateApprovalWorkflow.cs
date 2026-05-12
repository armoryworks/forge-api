using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;

using Forge.Core.Entities;
using Forge.Core.Enums;
using Forge.Core.Models;
using Forge.Data.Context;

namespace Forge.Api.Features.Approvals;

public record UpdateApprovalWorkflowCommand(int Id, CreateApprovalWorkflowRequestModel Data) : IRequest<ApprovalWorkflowResponseModel>;

public class UpdateApprovalWorkflowValidator : AbstractValidator<UpdateApprovalWorkflowCommand>
{
    public UpdateApprovalWorkflowValidator()
    {
        RuleFor(x => x.Data.Name).NotEmpty().MaximumLength(200);
        RuleFor(x => x.Data.EntityType).NotEmpty().MaximumLength(50);
        RuleFor(x => x.Data.Steps).NotEmpty();
    }
}

public class UpdateApprovalWorkflowHandler(AppDbContext db)
    : IRequestHandler<UpdateApprovalWorkflowCommand, ApprovalWorkflowResponseModel>
{
    public async Task<ApprovalWorkflowResponseModel> Handle(UpdateApprovalWorkflowCommand request, CancellationToken ct)
    {
        var workflow = await db.ApprovalWorkflows
            .Include(w => w.Steps)
            .FirstOrDefaultAsync(w => w.Id == request.Id, ct)
            ?? throw new KeyNotFoundException($"Approval workflow {request.Id} not found.");

        var data = request.Data;
        workflow.Name = data.Name.Trim();
        workflow.EntityType = data.EntityType.Trim();
        workflow.Description = data.Description?.Trim();
        workflow.ActivationConditionsJson = data.ActivationConditionsJson;

        db.ApprovalSteps.RemoveRange(workflow.Steps);

        foreach (var stepData in data.Steps)
        {
            if (!Enum.TryParse<ApproverType>(stepData.ApproverType, true, out var approverType))
                throw new InvalidOperationException($"Invalid approver type '{stepData.ApproverType}'.");

            workflow.Steps.Add(new ApprovalStep
            {
                StepNumber = stepData.StepNumber,
                Name = stepData.Name.Trim(),
                ApproverType = approverType,
                ApproverUserId = stepData.ApproverUserId,
                ApproverRole = stepData.ApproverRole?.Trim(),
                UseDirectManager = stepData.UseDirectManager,
                AutoApproveBelow = stepData.AutoApproveBelow,
                EscalationHours = stepData.EscalationHours,
                RequireComments = stepData.RequireComments,
                AllowDelegation = stepData.AllowDelegation,
            });
        }

        await db.SaveChangesAsync(ct);

        return new ApprovalWorkflowResponseModel(
            workflow.Id, workflow.Name, workflow.EntityType, workflow.IsActive,
            workflow.Description, workflow.ActivationConditionsJson,
            workflow.Steps.OrderBy(s => s.StepNumber).Select(s => new ApprovalStepResponseModel(
                s.Id, s.StepNumber, s.Name, s.ApproverType.ToString(),
                s.ApproverUserId, null, s.ApproverRole, s.UseDirectManager,
                s.AutoApproveBelow, s.EscalationHours, s.RequireComments, s.AllowDelegation
            )).ToList(),
            workflow.CreatedAt
        );
    }
}
