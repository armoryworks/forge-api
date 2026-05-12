using System.Security.Claims;

using FluentValidation;
using MediatR;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;

using Forge.Core.Enums;
using Forge.Core.Interfaces;
using Forge.Core.Models;
using Forge.Data.Context;

namespace Forge.Api.Features.Quality;

public record DispositionNcrCommand(int Id, DispositionNcrRequestModel Request) : IRequest;

public class DispositionNcrValidator : AbstractValidator<DispositionNcrCommand>
{
    public DispositionNcrValidator()
    {
        RuleFor(x => x.Request.Code).IsInEnum();
        RuleFor(x => x.Request.ReworkInstructions)
            .NotEmpty()
            .When(x => x.Request.Code == NcrDispositionCode.Rework)
            .WithMessage("Rework instructions are required when disposition is Rework");
    }
}

public class DispositionNcrHandler(
    AppDbContext db,
    IClock clock,
    IHttpContextAccessor httpContextAccessor)
    : IRequestHandler<DispositionNcrCommand>
{
    public async Task Handle(DispositionNcrCommand command, CancellationToken cancellationToken)
    {
        var ncr = await db.NonConformances
            .FirstOrDefaultAsync(n => n.Id == command.Id, cancellationToken)
            ?? throw new KeyNotFoundException($"NCR {command.Id} not found");

        var userId = int.Parse(httpContextAccessor.HttpContext!.User.FindFirstValue(ClaimTypes.NameIdentifier)!);

        ncr.DispositionCode = command.Request.Code;
        ncr.DispositionNotes = command.Request.Notes;
        ncr.ReworkInstructions = command.Request.ReworkInstructions;
        ncr.DispositionById = userId;
        ncr.DispositionAt = clock.UtcNow;
        ncr.Status = NcrStatus.Dispositioned;

        await db.SaveChangesAsync(cancellationToken);
    }
}
