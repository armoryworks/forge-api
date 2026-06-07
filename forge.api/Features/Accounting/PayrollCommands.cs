using System.Security.Claims;

using MediatR;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;

using Forge.Api.Capabilities;
using Forge.Core.Interfaces;
using Forge.Core.Models.Accounting;
using Forge.Data.Context;

namespace Forge.Api.Features.Accounting;

/// <summary>Phase-5 — create a pay run. CAP-PAYROLL-RUN gated.</summary>
[RequiresCapability("CAP-PAYROLL-RUN")]
public record CreatePayRunCommand(CreatePayRunModel Model) : IRequest<PayRunModel>;

public class CreatePayRunHandler(IPayrollService service) : IRequestHandler<CreatePayRunCommand, PayRunModel>
{
    public Task<PayRunModel> Handle(CreatePayRunCommand request, CancellationToken ct)
        => service.CreatePayRunAsync(request.Model, ct);
}

/// <summary>Phase-5 — list a book's pay runs. CAP-PAYROLL-RUN gated.</summary>
[RequiresCapability("CAP-PAYROLL-RUN")]
public record ListPayRunsQuery(int BookId) : IRequest<IReadOnlyList<PayRunModel>>;

public class ListPayRunsHandler(IPayrollService service) : IRequestHandler<ListPayRunsQuery, IReadOnlyList<PayRunModel>>
{
    public Task<IReadOnlyList<PayRunModel>> Handle(ListPayRunsQuery request, CancellationToken ct)
        => service.ListAsync(request.BookId, ct);
}

/// <summary>Phase-5 — post the payroll journal for a pay run. CAP-PAYROLL-RUN gated.</summary>
[RequiresCapability("CAP-PAYROLL-RUN")]
public record PostPayRunCommand(int PayRunId) : IRequest<PayRunModel>;

public class PostPayRunHandler(
    IPayrollService service,
    IHttpContextAccessor? httpContextAccessor = null,
    AppDbContext? db = null)
    : IRequestHandler<PostPayRunCommand, PayRunModel>
{
    public async Task<PayRunModel> Handle(PostPayRunCommand request, CancellationToken ct)
    {
        var userId = int.TryParse(
            httpContextAccessor?.HttpContext?.User.FindFirstValue(ClaimTypes.NameIdentifier),
            out var uid) ? uid : 0;

        await using var tx = db is not null ? await db.Database.BeginTransactionAsync(ct) : null;
        var result = await service.PostPayRunAsync(request.PayRunId, userId, ct);
        if (tx is not null) await tx.CommitAsync(ct);
        return result;
    }
}
