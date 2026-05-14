using MediatR;
using Forge.Core.Interfaces;
using Forge.Core.Models;

namespace Forge.Api.Features.Expenses;

public record GetExpensesQuery(int? UserId, ExpenseStatus? Status, string? Search) : IRequest<List<ExpenseResponseModel>>;

public class GetExpensesHandler(IExpenseRepository repo) : IRequestHandler<GetExpensesQuery, List<ExpenseResponseModel>>
{
    public Task<List<ExpenseResponseModel>> Handle(GetExpensesQuery request, CancellationToken cancellationToken)
        => repo.GetExpensesAsync(request.UserId, request.Status, request.Search, cancellationToken);
}
