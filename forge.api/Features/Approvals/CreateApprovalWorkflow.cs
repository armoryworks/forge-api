using FluentValidation;
using MediatR;

using Forge.Core.Entities;
using Forge.Core.Models;
using Forge.Data.Context;

namespace Forge.Api.Features.Approvals;

public record CreateApprovalWorkflowCommand(CreateApprovalWorkflowRequestModel Data) : IRequest<ApprovalWorkflowResponseModel>;

public class CreateApprovalWorkflowValidator : AbstractValidator<CreateApprovalWorkflowCommand>
{
    public CreateApprovalWorkflowValidator()
    {
        RuleFor(x => x.Data.Name).NotEmpty().MaximumLength(200);
        RuleFor(x => x.Data.EntityType).NotEmpty().MaximumLength(50);
        RuleFor(x => x.Data.Steps).NotEmpty();
        RuleForEach(x => x.Data.Steps).ChildRules(step =>
        {
            step.RuleFor(s => s.Name).NotEmpty().MaximumLength(200);
            step.RuleFor(s => s.StepNumber).GreaterThan(0);
        });
    }
}

public class CreateApprovalWorkflowHandler(AppDbContext db)
    : IRequestHandler<CreateApprovalWorkflowCommand, ApprovalWorkflowResponseModel>
{
    public async Task<ApprovalWorkflowResponseModel> Handle(CreateApprovalWorkflowCommand request, CancellationToken ct)
    {
        var data = request.Data;
        var workflow = new ApprovalWorkflow
        {
            Name = data.Name.Trim(),
            EntityType = data.EntityType.Trim(),
            Description = data.Description?.Trim(),
            ActivationConditionsJson = data.ActivationConditionsJson,
            IsActive = true,
        };

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

        db.ApprovalWorkflows.Add(workflow);
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
