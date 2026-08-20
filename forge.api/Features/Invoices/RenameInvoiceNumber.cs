using FluentValidation;
using MediatR;
using Forge.Core.Enums;
using Forge.Core.Interfaces;
using Forge.Data.Context;
using Forge.Data.Extensions;

namespace Forge.Api.Features.Invoices;

/// <summary>
/// Changes an invoice's human-readable number while it is still Draft (before it has been sent or
/// posted). Invoices are otherwise immutable, so this is the only edit path for the number. The old
/// value stays resolvable through the business-identifier registry.
/// </summary>
public record RenameInvoiceNumberCommand(int Id, string InvoiceNumber) : IRequest;

public class RenameInvoiceNumberCommandValidator : AbstractValidator<RenameInvoiceNumberCommand>
{
    public RenameInvoiceNumberCommandValidator()
    {
        RuleFor(x => x.InvoiceNumber).NotEmpty().MaximumLength(50);
    }
}

public class RenameInvoiceNumberHandler(
    IInvoiceRepository repo,
    ISystemSettingRepository systemSettings,
    IBusinessIdentifierService identifiers,
    AppDbContext db) : IRequestHandler<RenameInvoiceNumberCommand>
{
    private const string AllowManualInvoiceNumbersKey = "invoices.allow_manual_numbers";

    public async Task Handle(RenameInvoiceNumberCommand request, CancellationToken cancellationToken)
    {
        var invoice = await repo.FindAsync(request.Id, cancellationToken)
            ?? throw new KeyNotFoundException($"Invoice {request.Id} not found");

        var newNumber = request.InvoiceNumber.Trim();
        if (newNumber == invoice.InvoiceNumber)
            return;

        if (invoice.Status != InvoiceStatus.Draft)
            throw new InvalidOperationException("An invoice's number can only be changed while it is Draft.");

        if (!await ManualInvoiceNumbersAllowedAsync(cancellationToken))
            throw new InvalidOperationException("Manual invoice numbers are disabled.");

        if (await repo.InvoiceNumberExistsAsync(newNumber, invoice.Id, cancellationToken))
            throw new InvalidOperationException($"Invoice number '{newNumber}' is already in use.");

        await identifiers.IssueAsync(BusinessEntityType.Invoice, invoice.Id, invoice.InvoiceNumber, cancellationToken);
        await identifiers.RenameAsync(BusinessEntityType.Invoice, invoice.Id, newNumber, cancellationToken);

        var oldNumber = invoice.InvoiceNumber;
        invoice.InvoiceNumber = newNumber;

        db.LogActivityAt("updated", $"Renamed invoice number {oldNumber} → {newNumber}", ("Invoice", invoice.Id));
        await repo.SaveChangesAsync(cancellationToken);
    }

    private async Task<bool> ManualInvoiceNumbersAllowedAsync(CancellationToken ct)
    {
        var setting = await systemSettings.FindByKeyAsync(AllowManualInvoiceNumbersKey, ct);
        return setting is not null && bool.TryParse(setting.Value, out var enabled) && enabled;
    }
}
