using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;

using Forge.Core.Interfaces;
using Forge.Data.Context;

namespace Forge.Api.Features.Mrp;

public record ResolveMrpExceptionCommand(int Id, string? ResolutionNotes, int? ResolvedByUserId) : IRequest;

public class ResolveMrpExceptionValidator : AbstractValidator<ResolveMrpExceptionCommand>
{
    public ResolveMrpExceptionValidator()
    {
        RuleFor(x => x.Id).GreaterThan(0);
        RuleFor(x => x.ResolutionNotes).MaximumLength(2000);
    }
}

public class ResolveMrpExceptionHandler(AppDbContext db, IClock clock)
    : IRequestHandler<ResolveMrpExceptionCommand>
{
    public async Task Handle(ResolveMrpExceptionCommand request, CancellationToken cancellationToken)
    {
        var exception = await db.MrpExceptions
            .FirstOrDefaultAsync(e => e.Id == request.Id, cancellationToken)
            ?? throw new KeyNotFoundException($"MRP exception {request.Id} not found.");

        exception.IsResolved = true;
        exception.ResolvedAt = clock.UtcNow;
        exception.ResolvedByUserId = request.ResolvedByUserId;
        exception.ResolutionNotes = request.ResolutionNotes;

        await db.SaveChangesAsync(cancellationToken);
    }
}
