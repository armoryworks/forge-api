using System.Security.Claims;

using FluentValidation;
using MediatR;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;

using Forge.Core.Enums;
using Forge.Core.Interfaces;
using Forge.Core.Models;
using Forge.Data.Context;
using Forge.Data.Extensions;

namespace Forge.Api.Features.Quality;

public record ContainNcrCommand(int Id, ContainNcrRequestModel Request) : IRequest;

public class ContainNcrValidator : AbstractValidator<ContainNcrCommand>
{
    public ContainNcrValidator()
    {
        RuleFor(x => x.Request.ContainmentActions).MaximumLength(4000);
    }
}

public class ContainNcrHandler(
    AppDbContext db,
    IClock clock,
    IHttpContextAccessor httpContextAccessor)
    : IRequestHandler<ContainNcrCommand>
{
    public async Task Handle(ContainNcrCommand command, CancellationToken cancellationToken)
    {
        var ncr = await db.NonConformances
            .FirstOrDefaultAsync(n => n.Id == command.Id, cancellationToken)
            ?? throw new KeyNotFoundException($"NCR {command.Id} not found");

        if (ncr.Status is not (NcrStatus.Open or NcrStatus.UnderReview))
            throw new InvalidOperationException(
                $"NCR {ncr.NcrNumber} is {ncr.Status}; only an Open or Under Review NCR can be contained.");

        if (!string.IsNullOrWhiteSpace(command.Request.ContainmentActions))
            ncr.ContainmentActions = command.Request.ContainmentActions;

        ncr.ContainmentById = int.Parse(httpContextAccessor.HttpContext!.User.FindFirstValue(ClaimTypes.NameIdentifier)!);
        ncr.ContainmentAt = clock.UtcNow;
        ncr.Status = NcrStatus.Contained;

        db.LogActivityAt("contained", $"Contained {ncr.NcrNumber}", ("NonConformance", ncr.Id));

        await db.SaveChangesAsync(cancellationToken);
    }
}
