using FluentValidation;
using MediatR;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;

using Forge.Api.Features.Files;
using Forge.Core.Entities;
using Forge.Core.Enums;
using Forge.Core.Interfaces;
using Forge.Data.Context;
using Forge.Data.Extensions;

namespace Forge.Api.Features.SalesOrders.Acceptance;

/// <summary>
/// The 80% capture flow: staff records an offline customer acceptance (signed document received by
/// upload / fax / email, or a verbal acceptance). Uploads the evidence tagged as CustomerAcceptance
/// and writes an Accepted record that satisfies the production gate. A document is required except for
/// Verbal (weakest, note-only).
/// </summary>
public record RecordManualAcceptanceCommand(int SalesOrderId, AcceptanceMethod Method, string? Note, IFormFile? File)
    : IRequest<SalesOrderAcceptanceResponseModel>;

public class RecordManualAcceptanceValidator : AbstractValidator<RecordManualAcceptanceCommand>
{
    private static readonly AcceptanceMethod[] Offline =
        [AcceptanceMethod.ManualUpload, AcceptanceMethod.Fax, AcceptanceMethod.Email, AcceptanceMethod.Verbal];

    public RecordManualAcceptanceValidator()
    {
        RuleFor(x => x.SalesOrderId).GreaterThan(0);
        RuleFor(x => x.Method)
            .Must(m => Offline.Contains(m))
            .WithMessage("Method must be one of ManualUpload, Fax, Email, Verbal.");
        RuleFor(x => x.File)
            .NotNull().When(x => x.Method != AcceptanceMethod.Verbal)
            .WithMessage("A signed document is required for this acceptance method.");
        RuleFor(x => x.Note)
            .NotEmpty().When(x => x.Method == AcceptanceMethod.Verbal)
            .WithMessage("A note describing the verbal acceptance is required.");
    }
}

public class RecordManualAcceptanceHandler(AppDbContext db, IMediator mediator, IClock clock)
    : IRequestHandler<RecordManualAcceptanceCommand, SalesOrderAcceptanceResponseModel>
{
    public async Task<SalesOrderAcceptanceResponseModel> Handle(RecordManualAcceptanceCommand request, CancellationToken cancellationToken)
    {
        var soExists = await db.SalesOrders.AnyAsync(o => o.Id == request.SalesOrderId, cancellationToken);
        if (!soExists)
            throw new KeyNotFoundException($"Sales order {request.SalesOrderId} not found.");

        int? fileId = null;
        if (request.File is not null)
        {
            // Reuse the generic upload pipeline, tagging the file so it reads as acceptance evidence.
            var uploaded = await mediator.Send(
                new UploadFileCommand("sales-orders", request.SalesOrderId, request.File, "CustomerAcceptance"),
                cancellationToken);
            fileId = uploaded.Id;
        }

        var acceptance = new SalesOrderAcceptance
        {
            SalesOrderId = request.SalesOrderId,
            Status = AcceptanceStatus.Accepted,
            Method = request.Method,
            FileAttachmentId = fileId,
            RecordedByUserId = db.CurrentUserId,
            Note = request.Note,
            AcceptedAt = clock.UtcNow,
        };
        db.SalesOrderAcceptances.Add(acceptance);

        db.LogActivityAt("so-acceptance-recorded",
            $"Customer acceptance recorded ({request.Method})", ("SalesOrder", request.SalesOrderId));
        await db.SaveChangesAsync(cancellationToken);

        return await AcceptanceQuery.ByIdAsync(db, acceptance.Id, cancellationToken);
    }
}
