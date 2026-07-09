using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;
using QuestPDF.Fluent;

using Forge.Core.Entities;
using Forge.Core.Enums;
using Forge.Core.Interfaces;
using Forge.Data.Context;
using Forge.Data.Extensions;

namespace Forge.Api.Features.SalesOrders.Acceptance;

/// <summary>
/// E-signature channel — send the Sales Order to the customer for signature via the pluggable signing
/// provider (DocuSeal today; any <see cref="IDocumentSigningService"/> impl works). Renders the SO
/// acceptance PDF, opens a submission, and records a Pending ESignature acceptance that turns Accepted
/// once the customer signs (see CompleteSignatureAcceptance).
/// </summary>
public record SendSalesOrderForSignatureCommand(int SalesOrderId, string SignerEmail, string SignerName)
    : IRequest<SendForSignatureResponseModel>;

public record SendForSignatureResponseModel(SalesOrderAcceptanceResponseModel Acceptance, string SubmitUrl);

public class SendSalesOrderForSignatureValidator : AbstractValidator<SendSalesOrderForSignatureCommand>
{
    public SendSalesOrderForSignatureValidator()
    {
        RuleFor(x => x.SalesOrderId).GreaterThan(0);
        RuleFor(x => x.SignerEmail).NotEmpty().EmailAddress();
        RuleFor(x => x.SignerName).NotEmpty().MaximumLength(200);
    }
}

public class SendSalesOrderForSignatureHandler(
    AppDbContext db, IDocumentSigningService signing, ISystemSettingRepository settings, IClock clock)
    : IRequestHandler<SendSalesOrderForSignatureCommand, SendForSignatureResponseModel>
{
    public async Task<SendForSignatureResponseModel> Handle(SendSalesOrderForSignatureCommand request, CancellationToken cancellationToken)
    {
        var order = await db.SalesOrders
            .Include(o => o.Customer)
            .Include(o => o.Lines)
            .FirstOrDefaultAsync(o => o.Id == request.SalesOrderId, cancellationToken)
            ?? throw new KeyNotFoundException($"Sales order {request.SalesOrderId} not found.");

        var companyName = (await settings.FindByKeyAsync("company.name", cancellationToken))?.Value ?? "Forge";
        var lines = order.Lines
            .Select(l => new SalesOrderAcceptancePdfDocument.Line(l.Description, l.Quantity, l.UnitPrice))
            .ToList();

        var pdfBytes = new SalesOrderAcceptancePdfDocument(
            companyName, order.OrderNumber, order.Customer?.Name ?? "Customer", null, lines).GeneratePdf();

        var templateId = await signing.CreateTemplateFromPdfAsync($"SO-Acceptance-{order.OrderNumber}", pdfBytes, cancellationToken);
        var submission = await signing.CreateSubmissionAsync(templateId, request.SignerEmail, request.SignerName, cancellationToken);

        var acceptance = new SalesOrderAcceptance
        {
            SalesOrderId = order.Id,
            Status = AcceptanceStatus.Pending,
            Method = AcceptanceMethod.ESignature,
            Provider = "DocuSeal",
            ProviderReference = submission.SubmissionId.ToString(),
            SentTo = request.SignerEmail,
            AcceptedByName = request.SignerName,
            RecordedByUserId = db.CurrentUserId,
            ExpiresAt = clock.UtcNow.AddDays(30),
        };
        db.SalesOrderAcceptances.Add(acceptance);
        db.LogActivityAt("so-acceptance-sent",
            $"Sales order sent to {request.SignerEmail} for e-signature", ("SalesOrder", order.Id));
        await db.SaveChangesAsync(cancellationToken);

        var model = await AcceptanceQuery.ByIdAsync(db, acceptance.Id, cancellationToken);
        return new SendForSignatureResponseModel(model, submission.SubmitUrl);
    }
}
