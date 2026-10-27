using System.Globalization;

using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;

using Forge.Api.Capabilities;
using Forge.Api.Middleware;
using Forge.Core.Models.Accounting;
using Forge.Data.Context;
using Forge.Data.Extensions;

namespace Forge.Api.Features.Accounting;

/// <summary>
/// Sets a book's maker-checker threshold (null = off; manual entries above it wait for a second approver) and
/// its reporting time zone. The zone is refused with 409 <c>book-timezone-locked</c> once the book has a
/// posted entry.
/// </summary>
[RequiresCapability("CAP-ACCT-FULLGL")]
public record UpdateBookSettingsCommand(int BookId, decimal? MakerCheckerThreshold, string? ReportingTimeZone)
    : IRequest<BookSettingsModel>;

public class UpdateBookSettingsValidator : AbstractValidator<UpdateBookSettingsCommand>
{
    public UpdateBookSettingsValidator()
    {
        RuleFor(x => x.BookId).GreaterThan(0);
        RuleFor(x => x.MakerCheckerThreshold)
            .GreaterThanOrEqualTo(0m)
            .When(x => x.MakerCheckerThreshold is not null)
            .WithMessage("The maker-checker threshold cannot be negative.")
            .WithErrorCode("maker-checker-threshold-negative");
        RuleFor(x => x.ReportingTimeZone)
            .Must(BookTimeZoneRules.IsKnownTimeZone)
            .When(x => x.ReportingTimeZone is not null)
            .WithMessage("The reporting time zone is not a known IANA time zone.")
            .WithErrorCode("time-zone-unknown");
    }
}

public class UpdateBookSettingsHandler(AppDbContext db)
    : IRequestHandler<UpdateBookSettingsCommand, BookSettingsModel>
{
    public async Task<BookSettingsModel> Handle(UpdateBookSettingsCommand request, CancellationToken ct)
    {
        var book = await db.Books.FirstOrDefaultAsync(b => b.Id == request.BookId, ct)
            ?? throw new KeyNotFoundException($"Book {request.BookId} not found.");

        var changes = new List<string>();
        if (book.MakerCheckerThreshold != request.MakerCheckerThreshold)
        {
            changes.Add(request.MakerCheckerThreshold is decimal limit
                ? $"maker-checker threshold {limit.ToString("0.00", CultureInfo.InvariantCulture)}"
                : "maker-checker threshold off");
            book.MakerCheckerThreshold = request.MakerCheckerThreshold;
        }

        var zone = request.ReportingTimeZone?.Trim();
        if (zone is not null && !string.Equals(zone, book.ReportingTimeZone, StringComparison.Ordinal))
        {
            if (await BookTimeZoneRules.HasPostedEntriesAsync(db, book.Id, ct))
                throw new CodedConflictException(
                    BookTimeZoneRules.LockedCode,
                    $"Book {book.Code} already has posted entries; its reporting time zone ({book.ReportingTimeZone}) can no longer change.");
            changes.Add($"reporting time zone {book.ReportingTimeZone} → {zone}");
            book.ReportingTimeZone = zone;
        }

        if (changes.Count > 0)
        {
            db.LogActivityAt("book-settings-updated", $"Book {book.Code} settings: {string.Join("; ", changes)}", ("Book", book.Id));
            await db.SaveChangesAsync(ct);
        }

        return await GetBookSettingsHandler.BuildAsync(db, book, ct);
    }
}
