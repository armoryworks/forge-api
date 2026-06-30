using FluentValidation;
using MediatR;

using Forge.Core.Enums;
using Forge.Data.Context;
using Forge.Data.Extensions;

namespace Forge.Api.Features.CustomerReturns;

public record ResolveCustomerReturnCommand(int Id, string? InspectionNotes) : IRequest;

public class ResolveCustomerReturnValidator : AbstractValidator<ResolveCustomerReturnCommand>
{
    public ResolveCustomerReturnValidator()
    {
        RuleFor(x => x.InspectionNotes).MaximumLength(2000).When(x => x.InspectionNotes != null);
    }
}

public class ResolveCustomerReturnHandler(AppDbContext db)
    : IRequestHandler<ResolveCustomerReturnCommand>
{
    public async Task Handle(ResolveCustomerReturnCommand request, CancellationToken ct)
    {
        var ret = await db.CustomerReturns.FindAsync([request.Id], ct)
            ?? throw new KeyNotFoundException($"Customer return {request.Id} not found");

        if (ret.Status == CustomerReturnStatus.Closed)
            throw new InvalidOperationException("Cannot resolve a closed return");

        if (request.InspectionNotes != null)
            ret.InspectionNotes = request.InspectionNotes;

        ret.Status = CustomerReturnStatus.Resolved;

        db.LogActivityAt("resolved",
            $"Customer return {ret.ReturnNumber} resolved.",
            ("CustomerReturn", ret.Id));

        await db.SaveChangesAsync(ct);
    }
}
