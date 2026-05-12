using MediatR;
using Microsoft.EntityFrameworkCore;

using Forge.Core.Models;
using Forge.Data.Context;

namespace Forge.Api.Features.Projects;

public record UpdateProjectCommand(int Id, UpdateProjectRequestModel Request) : IRequest<ProjectResponseModel>;

public class UpdateProjectHandler(AppDbContext db) : IRequestHandler<UpdateProjectCommand, ProjectResponseModel>
{
    public async Task<ProjectResponseModel> Handle(UpdateProjectCommand command, CancellationToken cancellationToken)
    {
        var project = await db.Projects
            .Include(p => p.Customer)
            .FirstOrDefaultAsync(p => p.Id == command.Id, cancellationToken)
            ?? throw new KeyNotFoundException($"Project {command.Id} not found");

        var req = command.Request;
        if (req.Name != null) project.Name = req.Name;
        if (req.Description != null) project.Description = req.Description;
        if (req.CustomerId.HasValue) project.CustomerId = req.CustomerId;
        if (req.BudgetTotal.HasValue) project.BudgetTotal = req.BudgetTotal.Value;
        if (req.Status.HasValue) project.Status = req.Status.Value;
        if (req.PlannedStartDate.HasValue) project.PlannedStartDate = req.PlannedStartDate;
        if (req.PlannedEndDate.HasValue) project.PlannedEndDate = req.PlannedEndDate;
        if (req.ActualStartDate.HasValue) project.ActualStartDate = req.ActualStartDate;
        if (req.ActualEndDate.HasValue) project.ActualEndDate = req.ActualEndDate;
        if (req.PercentComplete.HasValue) project.PercentComplete = req.PercentComplete;
        if (req.Notes != null) project.Notes = req.Notes;

        await db.SaveChangesAsync(cancellationToken);

        return new ProjectResponseModel
        {
            Id = project.Id,
            ProjectNumber = project.ProjectNumber,
            Name = project.Name,
            Description = project.Description,
            CustomerId = project.CustomerId,
            CustomerName = project.Customer?.CompanyName,
            SalesOrderId = project.SalesOrderId,
            BudgetTotal = project.BudgetTotal,
            ActualTotal = project.ActualTotal,
            CommittedTotal = project.CommittedTotal,
            EstimateAtCompletionTotal = project.EstimateAtCompletionTotal,
            Status = project.Status.ToString(),
            PlannedStartDate = project.PlannedStartDate,
            PlannedEndDate = project.PlannedEndDate,
            ActualStartDate = project.ActualStartDate,
            ActualEndDate = project.ActualEndDate,
            PercentComplete = project.PercentComplete,
            Notes = project.Notes,
            CreatedAt = project.CreatedAt,
        };
    }
}
