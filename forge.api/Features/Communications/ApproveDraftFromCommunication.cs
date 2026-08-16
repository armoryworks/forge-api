using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;

using Forge.Core.Entities;
using Forge.Core.Enums;
using Forge.Core.Interfaces;
using Forge.Core.Models;
using Forge.Data.Context;
using Forge.Data.Extensions;

namespace Forge.Api.Features.Communications;

/// <summary>
/// The human's commit point. Turns a reviewed communication into a draft sales
/// order and records the attestation that authorizes it.
///
/// <para><b>Nothing upstream of this creates an order.</b> Ingestion proposes;
/// this is where a person decides. The command takes the reviewer's own line
/// values rather than the extracted ones — what they approve is what they see on
/// screen, which may be corrected, and taking the extraction here would make the
/// review cosmetic.</para>
///
/// <para>The resulting order carries <c>AuthorizingAttestationId</c>, and the
/// attestation carries the artifact and the communication. That triple is what
/// renders the Authorized-by line: the document, its hash, when it arrived, from
/// whom, and a route back to the original message.</para>
/// </summary>
public record ApproveDraftFromCommunicationCommand(
    int CommunicationId,
    int CustomerId,
    /// <summary>The artifact the customer's authorization actually is — usually the PO PDF, sometimes the message itself.</summary>
    int AuthorizingArtifactId,
    string? CustomerPo,
    DateTimeOffset? RequestedDeliveryDate,
    decimal TaxRate,
    List<CreateSalesOrderLineModel> Lines,
    /// <summary>The standing agreement this leans on, when the reviewer identified one.</summary>
    int? SupportedByAttestationId = null,
    string? Note = null) : IRequest<ApproveDraftResult>;

public record ApproveDraftResult(int SalesOrderId, string OrderNumber, int AttestationId);

public class ApproveDraftFromCommunicationValidator : AbstractValidator<ApproveDraftFromCommunicationCommand>
{
    public ApproveDraftFromCommunicationValidator()
    {
        RuleFor(x => x.CommunicationId).GreaterThan(0);
        RuleFor(x => x.CustomerId).GreaterThan(0);
        RuleFor(x => x.AuthorizingArtifactId).GreaterThan(0)
            .WithMessage("An authorizing document is required — that is the whole point of the record.");
        RuleFor(x => x.TaxRate).GreaterThanOrEqualTo(0).LessThan(1);
        RuleFor(x => x.Lines).NotEmpty();
        RuleForEach(x => x.Lines).ChildRules(line =>
        {
            line.RuleFor(l => l.Description).NotEmpty();
            line.RuleFor(l => l.Quantity).GreaterThan(0m);
            line.RuleFor(l => l.UnitPrice).GreaterThanOrEqualTo(0m);
        });
    }
}

public class ApproveDraftFromCommunicationHandler(
    AppDbContext db,
    ISalesOrderRepository orderRepo,
    IBarcodeService barcodeService,
    IClock clock)
    : IRequestHandler<ApproveDraftFromCommunicationCommand, ApproveDraftResult>
{
    public async Task<ApproveDraftResult> Handle(
        ApproveDraftFromCommunicationCommand request, CancellationToken ct)
    {
        var communication = await db.Communications
            .FirstOrDefaultAsync(c => c.Id == request.CommunicationId, ct)
            ?? throw new KeyNotFoundException($"Communication {request.CommunicationId} not found");

        var artifact = await db.CommunicationArtifacts
            .AsNoTracking()
            .FirstOrDefaultAsync(a => a.Id == request.AuthorizingArtifactId, ct)
            ?? throw new KeyNotFoundException($"Artifact {request.AuthorizingArtifactId} not found");

        if (artifact.CommunicationId != communication.Id)
        {
            // The evidence must come from the message being approved. Allowing
            // an arbitrary artifact would let an order cite a document that
            // arrived in an unrelated conversation.
            throw new InvalidOperationException(
                $"Artifact {artifact.Id} belongs to a different communication and cannot authorize this order.");
        }

        if (!await db.Customers.AnyAsync(c => c.Id == request.CustomerId, ct))
            throw new KeyNotFoundException($"Customer {request.CustomerId} not found");

        var orderNumber = await orderRepo.GenerateNextOrderNumberAsync(ct);

        var order = new SalesOrder
        {
            OrderNumber = orderNumber,
            CustomerId = request.CustomerId,
            CustomerPO = request.CustomerPo,
            RequestedDeliveryDate = request.RequestedDeliveryDate,
            TaxRate = request.TaxRate,
            // Draft, not Confirmed. Approving the read of the message is not the
            // same as releasing the order to production — that stays a separate,
            // deliberate act on the order itself.
            Status = SalesOrderStatus.Draft,
            Notes = request.Note,
        };

        var lineNumber = 1;
        foreach (var line in request.Lines)
        {
            order.Lines.Add(new SalesOrderLine
            {
                PartId = line.PartId,
                Description = line.Description,
                Quantity = line.Quantity,
                UnitPrice = line.UnitPrice,
                LineNumber = lineNumber++,
                Notes = line.Notes,
            });
        }

        await orderRepo.AddAsync(order, ct);
        await orderRepo.SaveChangesAsync(ct);

        var attestation = new Attestation
        {
            SalesOrderId = order.Id,
            PartyType = CommunicationPartyType.Customer,
            PartyId = request.CustomerId,
            StatementType = AttestationStatementType.PurchaseOrder,
            Status = AcceptanceStatus.Accepted,
            // The customer sent their own instrument; we received it by email.
            Method = AcceptanceMethod.Email,
            ArtifactId = artifact.Id,
            CommunicationId = communication.Id,
            SupportedByAttestationId = request.SupportedByAttestationId,
            // When the party stated it, versus when we acted on it. For an
            // emailed PO these differ by hours and the audit line quotes the former.
            CapturedAt = communication.OccurredAt,
            AcceptedAt = clock.UtcNow,
            RecordedByUserId = db.CurrentUserId,
            SentTo = communication.FromAddress,
            Note = request.Note,
        };

        db.Attestations.Add(attestation);
        await db.SaveChangesAsync(ct);

        order.AuthorizingAttestationId = attestation.Id;

        // The message is now filed against the order it produced, so the
        // Authorized-by line can route back to it.
        db.CommunicationLinks.Add(new CommunicationLink
        {
            CommunicationId = communication.Id,
            EntityType = CommunicationLink.Types.SalesOrder,
            EntityId = order.Id,
            PartyType = CommunicationPartyType.Customer,
            PartyId = request.CustomerId,
        });

        // A human has now looked at it, so it leaves the triage queue and
        // carries their name.
        communication.IsTriaged = true;
        communication.HandledByUserId ??= db.CurrentUserId;

        db.LogActivityAt(
            "draft-approved-from-communication",
            $"Draft order {orderNumber} approved from {communication.Channel} of "
                + $"{communication.OccurredAt:yyyy-MM-dd HH:mm} — authorized by "
                + $"{artifact.OriginalFilename ?? "artifact"} (sha256:{artifact.ShortHash})",
            ("SalesOrder", order.Id),
            ("Customer", request.CustomerId),
            ("Communication", communication.Id));

        await db.SaveChangesAsync(ct);

        await barcodeService.CreateBarcodeAsync(
            BarcodeEntityType.SalesOrder, order.Id, order.OrderNumber, ct);

        return new ApproveDraftResult(order.Id, orderNumber, attestation.Id);
    }
}
