using System.Security.Claims;

using MediatR;
using Microsoft.AspNetCore.Http;
using Forge.Api.Middleware;
using Forge.Core.Enums;
using Forge.Core.Interfaces;
using Forge.Core.Models;

namespace Forge.Api.Features.Expenses;

public record GetExpensesQuery(int? UserId, ExpenseStatus? Status, string? Search) : IRequest<List<ExpenseResponseModel>>;

public class GetExpensesHandler(IExpenseRepository repo, IHttpContextAccessor httpContext) : IRequestHandler<GetExpensesQuery, List<ExpenseResponseModel>>
{
    public Task<List<ExpenseResponseModel>> Handle(GetExpensesQuery request, CancellationToken cancellationToken)
    {
        var user = httpContext.HttpContext?.User;
        var userId = request.UserId;
        if (!ExpenseReviewers.Includes(user))
        {
            var callerId = int.TryParse(user?.FindFirstValue(ClaimTypes.NameIdentifier), out var id) ? id : 0;
            if (userId is not null && userId != callerId)
                throw new ForbiddenException("You can only see your own expenses.");
            userId = callerId;
        }

        return repo.GetExpensesAsync(userId, request.Status, request.Search, cancellationToken);
    }
}
