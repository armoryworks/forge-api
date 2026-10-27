using System.Security.Claims;

using FluentValidation;
using MediatR;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;

using Forge.Api.Capabilities;
using Forge.Api.Features.Accounting;
using Forge.Api.Features.VendorReturns;
using Forge.Core.Enums;
using Forge.Core.Interfaces;
using Forge.Core.Models;
using Forge.Data.Context;
using Forge.Data.Extensions;

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
        RuleFor(x => x.Request.Notes)
            .NotEmpty()
            .When(x => x.Request.Code is NcrDispositionCode.UseAsIs or NcrDispositionCode.Reject)
            .WithMessage("Notes are required when disposition is Use As Is or Reject");
    }
}

public class DispositionNcrHandler(
    AppDbContext db,
    IClock clock,
    IHttpContextAccessor httpContextAccessor,
    INcrScrapPostingService? scrapPosting = null,
    ICapabilitySnapshotProvider? capabilities = null)
    : IRequestHandler<DispositionNcrCommand>
{
    public async Task Handle(DispositionNcrCommand command, CancellationToken cancellationToken)
    {
        var ncr = await db.NonConformances
            .FirstOrDefaultAsync(n => n.Id == command.Id, cancellationToken)
            ?? throw new KeyNotFoundException($"NCR {command.Id} not found");

        if (ncr.Status is not (NcrStatus.Open or NcrStatus.UnderReview or NcrStatus.Contained))
            throw new InvalidOperationException(
                $"NCR {ncr.NcrNumber} is already {ncr.Status} and cannot be dispositioned again.");

        var userId = int.Parse(httpContextAccessor.HttpContext!.User.FindFirstValue(ClaimTypes.NameIdentifier)!);

        ncr.DispositionCode = command.Request.Code;
        ncr.DispositionNotes = command.Request.Notes;
        ncr.ReworkInstructions = command.Request.Code == NcrDispositionCode.Rework
            ? command.Request.ReworkInstructions
            : null;
        ncr.DispositionById = userId;
        ncr.DispositionAt = clock.UtcNow;
        ncr.Status = NcrStatus.Dispositioned;

        db.LogActivityAt(
            "dispositioned",
            $"Dispositioned as {command.Request.Code}",
            ("NonConformance", ncr.Id));

        if (command.Request.Code is NcrDispositionCode.UseAsIs or NcrDispositionCode.Rework)
            await LotQualityHold.ReleaseAsync(db, ncr, cancellationToken);

        if (command.Request.Code == NcrDispositionCode.ReturnToVendor && (capabilities?.IsEnabled("CAP-P2P-PO") ?? true))
        {
            await using var rtvTx = db.Database.CurrentTransaction is null
                ? await db.Database.BeginTransactionAsync(cancellationToken)
                : null;
            await db.SaveChangesAsync(cancellationToken);
            await VendorReturnFactory.OpenForNcrAsync(db, ncr, userId, cancellationToken);
            await db.SaveChangesAsync(cancellationToken);
            if (rtvTx is not null)
                await rtvTx.CommitAsync(cancellationToken);
            return;
        }

        if (command.Request.Code != NcrDispositionCode.Scrap)
        {
            await db.SaveChangesAsync(cancellationToken);
            return;
        }

        await using var tx = db.Database.CurrentTransaction is null
            ? await db.Database.BeginTransactionAsync(cancellationToken)
            : null;

        var scrapped = await LotQualityHold.ScrapHeldAsync(db, ncr, userId, ncr.DispositionAt.Value, cancellationToken);
        await db.SaveChangesAsync(cancellationToken);

        if (scrapPosting is not null && scrapped > 0)
            await scrapPosting.PostScrapAsync(
                ncr.Id, scrapped, DateOnly.FromDateTime(ncr.DispositionAt.Value.UtcDateTime), userId, cancellationToken);

        if (tx is not null)
            await tx.CommitAsync(cancellationToken);
    }
}
