using FluentValidation;
using MediatR;

using Forge.Data.Context;

namespace Forge.Api.Features.CustomerReturns;

public record UpdateCustomerReturnCommand(int Id, string? Reason, string? Notes, string? InspectionNotes) : IRequest;

public class UpdateCustomerReturnValidator : AbstractValidator<UpdateCustomerReturnCommand>
{
    public UpdateCustomerReturnValidator()
    {
        RuleFor(x => x.Reason).MaximumLength(1000).When(x => x.Reason != null);
        RuleFor(x => x.Notes).MaximumLength(2000).When(x => x.Notes != null);
        RuleFor(x => x.InspectionNotes).MaximumLength(2000).When(x => x.InspectionNotes != null);
    }
}

public class UpdateCustomerReturnHandler(AppDbContext db)
    : IRequestHandler<UpdateCustomerReturnCommand>
{
    public async Task Handle(UpdateCustomerReturnCommand request, CancellationToken ct)
    {
        var ret = await db.CustomerReturns.FindAsync([request.Id], ct)
            ?? throw new KeyNotFoundException($"Customer return {request.Id} not found");

        if (request.Reason != null) ret.Reason = request.Reason;
        if (request.Notes != null) ret.Notes = request.Notes;
        if (request.InspectionNotes != null) ret.InspectionNotes = request.InspectionNotes;

        await db.SaveChangesAsync(ct);
    }
}
