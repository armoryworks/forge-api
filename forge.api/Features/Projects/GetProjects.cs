using MediatR;
using Microsoft.EntityFrameworkCore;

using Forge.Core.Enums;
using Forge.Core.Models;
using Forge.Data.Context;

namespace Forge.Api.Features.Projects;

public record GetProjectsQuery(string? Status, int? CustomerId, int Page = 1, int PageSize = 25) : IRequest<object>;

public class GetProjectsHandler(AppDbContext db) : IRequestHandler<GetProjectsQuery, object>
{
    public async Task<object> Handle(GetProjectsQuery query, CancellationToken cancellationToken)
    {
        var q = db.Projects
            .AsNoTracking()
            .Include(p => p.Customer)
            .AsQueryable();

        if (!string.IsNullOrEmpty(query.Status) && Enum.TryParse<ProjectStatus>(query.Status, true, out var status))
            q = q.Where(p => p.Status == status);

        if (query.CustomerId.HasValue)
            q = q.Where(p => p.CustomerId == query.CustomerId.Value);

        var totalCount = await q.CountAsync(cancellationToken);

        var projects = await q
            .OrderByDescending(p => p.CreatedAt)
            .Skip((query.Page - 1) * query.PageSize)
            .Take(query.PageSize)
            .Select(p => new ProjectResponseModel
            {
                Id = p.Id,
                ProjectNumber = p.ProjectNumber,
                Name = p.Name,
                Description = p.Description,
                CustomerId = p.CustomerId,
                CustomerName = p.Customer != null ? p.Customer.CompanyName : null,
                SalesOrderId = p.SalesOrderId,
                BudgetTotal = p.BudgetTotal,
                ActualTotal = p.ActualTotal,
                CommittedTotal = p.CommittedTotal,
                EstimateAtCompletionTotal = p.EstimateAtCompletionTotal,
                Status = p.Status.ToString(),
                PlannedStartDate = p.PlannedStartDate,
                PlannedEndDate = p.PlannedEndDate,
                ActualStartDate = p.ActualStartDate,
                ActualEndDate = p.ActualEndDate,
                PercentComplete = p.PercentComplete,
                Notes = p.Notes,
                CreatedAt = p.CreatedAt,
            })
            .ToListAsync(cancellationToken);

        return new
        {
            data = projects,
            page = query.Page,
            pageSize = query.PageSize,
            totalCount,
            totalPages = (int)Math.Ceiling((double)totalCount / query.PageSize),
        };
    }
}
