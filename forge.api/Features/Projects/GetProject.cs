using MediatR;
using Microsoft.EntityFrameworkCore;

using Forge.Core.Models;
using Forge.Data.Context;

namespace Forge.Api.Features.Projects;

public record GetProjectQuery(int Id) : IRequest<ProjectResponseModel>;

public class GetProjectHandler(AppDbContext db) : IRequestHandler<GetProjectQuery, ProjectResponseModel>
{
    public async Task<ProjectResponseModel> Handle(GetProjectQuery query, CancellationToken cancellationToken)
    {
        var project = await db.Projects
            .AsNoTracking()
            .Include(p => p.Customer)
            .FirstOrDefaultAsync(p => p.Id == query.Id, cancellationToken)
            ?? throw new KeyNotFoundException($"Project {query.Id} not found");

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
