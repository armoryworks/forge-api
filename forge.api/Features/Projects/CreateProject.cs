using MediatR;
using Microsoft.EntityFrameworkCore;

using Forge.Core.Entities;
using Forge.Core.Models;
using Forge.Data.Context;

namespace Forge.Api.Features.Projects;

public record CreateProjectCommand(CreateProjectRequestModel Request) : IRequest<ProjectResponseModel>;

public class CreateProjectHandler(AppDbContext db) : IRequestHandler<CreateProjectCommand, ProjectResponseModel>
{
    public async Task<ProjectResponseModel> Handle(CreateProjectCommand command, CancellationToken cancellationToken)
    {
        var lastNumber = await db.Projects
            .AsNoTracking()
            .OrderByDescending(p => p.Id)
            .Select(p => p.ProjectNumber)
            .FirstOrDefaultAsync(cancellationToken);

        var nextNum = 1;
        if (lastNumber != null && lastNumber.StartsWith("PRJ-") && int.TryParse(lastNumber[4..], out var parsed))
            nextNum = parsed + 1;

        var project = new Project
        {
            ProjectNumber = $"PRJ-{nextNum:D4}",
            Name = command.Request.Name,
            Description = command.Request.Description,
            CustomerId = command.Request.CustomerId,
            SalesOrderId = command.Request.SalesOrderId,
            BudgetTotal = command.Request.BudgetTotal,
            Status = ProjectStatus.Planning,
            PlannedStartDate = command.Request.PlannedStartDate,
            PlannedEndDate = command.Request.PlannedEndDate,
            Notes = command.Request.Notes,
        };

        db.Projects.Add(project);
        await db.SaveChangesAsync(cancellationToken);

        return new ProjectResponseModel
        {
            Id = project.Id,
            ProjectNumber = project.ProjectNumber,
            Name = project.Name,
            Description = project.Description,
            CustomerId = project.CustomerId,
            SalesOrderId = project.SalesOrderId,
            BudgetTotal = project.BudgetTotal,
            ActualTotal = project.ActualTotal,
            CommittedTotal = project.CommittedTotal,
            EstimateAtCompletionTotal = project.EstimateAtCompletionTotal,
            Status = project.Status.ToString(),
            PlannedStartDate = project.PlannedStartDate,
            PlannedEndDate = project.PlannedEndDate,
            PercentComplete = project.PercentComplete,
            Notes = project.Notes,
            CreatedAt = project.CreatedAt,
        };
    }
}
