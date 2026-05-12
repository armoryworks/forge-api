using System.Security.Claims;

using MediatR;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;

using Forge.Core.Interfaces;
using Forge.Data.Context;

namespace Forge.Api.Features.Quality;

public record SignPpapPswCommand(int Id) : IRequest;

public class SignPpapPswHandler(AppDbContext db, IClock clock, IHttpContextAccessor httpContextAccessor)
    : IRequestHandler<SignPpapPswCommand>
{
    public async Task Handle(SignPpapPswCommand command, CancellationToken cancellationToken)
    {
        var submission = await db.PpapSubmissions
            .FirstOrDefaultAsync(s => s.Id == command.Id, cancellationToken)
            ?? throw new KeyNotFoundException($"PPAP submission {command.Id} not found");

        if (submission.PswSignedByUserId.HasValue)
            throw new InvalidOperationException("PSW has already been signed");

        var userId = int.Parse(httpContextAccessor.HttpContext!.User.FindFirstValue(ClaimTypes.NameIdentifier)!);

        submission.PswSignedByUserId = userId;
        submission.PswSignedAt = clock.UtcNow;

        await db.SaveChangesAsync(cancellationToken);
    }
}
