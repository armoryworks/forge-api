using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;

using Forge.Core.Enums;
using Forge.Core.Models;
using Forge.Data.Context;
using Forge.Data.Extensions;

namespace Forge.Api.Features.Quality;

public record ReopenNcrCommand(int Id, ReopenNcrRequestModel Request) : IRequest;

public class ReopenNcrValidator : AbstractValidator<ReopenNcrCommand>
{
    public ReopenNcrValidator()
    {
        RuleFor(x => x.Request.Reason)
            .NotEmpty()
            .WithMessage("A reason is required to reopen an NCR")
            .MaximumLength(2000);
    }
}

public class ReopenNcrHandler(AppDbContext db) : IRequestHandler<ReopenNcrCommand>
{
    public async Task Handle(ReopenNcrCommand command, CancellationToken cancellationToken)
    {
        var ncr = await db.NonConformances
            .FirstOrDefaultAsync(n => n.Id == command.Id, cancellationToken)
            ?? throw new KeyNotFoundException($"NCR {command.Id} not found");

        if (ncr.Status != NcrStatus.Closed)
            throw new InvalidOperationException(
                $"NCR {ncr.NcrNumber} is {ncr.Status}; only a Closed NCR can be reopened.");

        ncr.Status = NcrStatus.Dispositioned;

        db.LogActivityAt(
            "reopened",
            $"Reopened {ncr.NcrNumber}: {command.Request.Reason.Trim()}",
            ("NonConformance", ncr.Id));

        await db.SaveChangesAsync(cancellationToken);
    }
}
