using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;

using Forge.Core.Enums;
using Forge.Core.Models;
using Forge.Data.Context;
using Forge.Data.Extensions;

namespace Forge.Api.Features.Quality;

public record CloseNcrCommand(int Id, CloseNcrRequestModel Request) : IRequest;

public class CloseNcrValidator : AbstractValidator<CloseNcrCommand>
{
    public CloseNcrValidator()
    {
        RuleFor(x => x.Request.Notes).MaximumLength(2000);
    }
}

public class CloseNcrHandler(AppDbContext db) : IRequestHandler<CloseNcrCommand>
{
    public async Task Handle(CloseNcrCommand command, CancellationToken cancellationToken)
    {
        var ncr = await db.NonConformances
            .FirstOrDefaultAsync(n => n.Id == command.Id, cancellationToken)
            ?? throw new KeyNotFoundException($"NCR {command.Id} not found");

        if (ncr.Status != NcrStatus.Dispositioned)
            throw new InvalidOperationException(
                $"NCR {ncr.NcrNumber} is {ncr.Status}; only a Dispositioned NCR can be closed.");

        ncr.Status = NcrStatus.Closed;

        var notes = command.Request.Notes?.Trim();
        db.LogActivityAt(
            "closed",
            string.IsNullOrEmpty(notes) ? $"Closed {ncr.NcrNumber}" : $"Closed {ncr.NcrNumber}: {notes}",
            ("NonConformance", ncr.Id));

        await db.SaveChangesAsync(cancellationToken);
    }
}
