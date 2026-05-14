using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Forge.Core.Entities;
using Forge.Core.Interfaces;
using Forge.Core.Models;
using Forge.Data.Context;

namespace Forge.Api.Features.Invoices;

public record CreateInvoiceCommand(
    int CustomerId,
    int? SalesOrderId,
    int? ShipmentId,
    DateTimeOffset InvoiceDate,
    DateTimeOffset DueDate,
    string? CreditTerms,
    decimal TaxRate,
    string? Notes,
    List<CreateInvoiceLineModel> Lines,
    string? CustomerPO = null) : IRequest<InvoiceListItemModel>;

public class CreateInvoiceValidator : AbstractValidator<CreateInvoiceCommand>
{
    public CreateInvoiceValidator()
    {
        RuleFor(x => x.CustomerId).GreaterThan(0);
        RuleFor(x => x.Lines).NotEmpty().WithMessage("At least one line item is required");
        RuleFor(x => x.TaxRate).GreaterThanOrEqualTo(0).LessThan(1);
        RuleFor(x => x.DueDate).GreaterThanOrEqualTo(x => x.InvoiceDate);
        RuleFor(x => x.CustomerPO).MaximumLength(50);
        RuleForEach(x => x.Lines).ChildRules(line =>
        {
            line.RuleFor(l => l.Description).NotEmpty();
            // Phase 3 / WU-23 (F8-broad): decimal quantity supports fractional UoM.
            line.RuleFor(l => l.Quantity).GreaterThan(0m);
            line.RuleFor(l => l.UnitPrice).GreaterThanOrEqualTo(0);
        });
    }
}

public class CreateInvoiceHandler(
    IInvoiceRepository repo,
    ICustomerRepository customerRepo,
    AppDbContext db)
    : IRequestHandler<CreateInvoiceCommand, InvoiceListItemModel>
{
    public async Task<InvoiceListItemModel> Handle(CreateInvoiceCommand request, CancellationToken cancellationToken)
    {
        var customer = await customerRepo.FindAsync(request.CustomerId, cancellationToken)
            ?? throw new KeyNotFoundException($"Customer {request.CustomerId} not found");

        var invoiceNumber = await repo.GenerateNextInvoiceNumberAsync(cancellationToken);

        CreditTerms? creditTerms = request.CreditTerms != null
            ? Enum.Parse<CreditTerms>(request.CreditTerms, true)
            : null;

        // Propagate CustomerPO from the sourcing SO when the caller didn't
        // override. B2B customers reject invoices that don't echo their PO #.
        var customerPo = request.CustomerPO;
        if (customerPo is null && request.SalesOrderId is int soId)
        {
            customerPo = await db.SalesOrders
                .Where(so => so.Id == soId)
                .Select(so => so.CustomerPO)
                .FirstOrDefaultAsync(cancellationToken);
        }

        var invoice = new Invoice
        {
            InvoiceNumber = invoiceNumber,
            CustomerId = request.CustomerId,
            SalesOrderId = request.SalesOrderId,
            ShipmentId = request.ShipmentId,
            InvoiceDate = request.InvoiceDate,
            DueDate = request.DueDate,
            CreditTerms = creditTerms,
            TaxRate = request.TaxRate,
            Notes = request.Notes,
            CustomerPO = customerPo,
        };

        var lineNumber = 1;
        foreach (var line in request.Lines)
        {
            invoice.Lines.Add(new InvoiceLine
            {
                PartId = line.PartId,
                Description = line.Description,
                Quantity = line.Quantity,
                UnitPrice = line.UnitPrice,
                LineNumber = lineNumber++,
            });
        }

        await repo.AddAsync(invoice, cancellationToken);
        await repo.SaveChangesAsync(cancellationToken);

        var total = invoice.Lines.Sum(l => l.Quantity * l.UnitPrice) * (1 + invoice.TaxRate);

        return new InvoiceListItemModel(
            invoice.Id, invoice.InvoiceNumber, invoice.CustomerId, customer.Name,
            invoice.Status.ToString(), invoice.InvoiceDate, invoice.DueDate,
            total, 0, total, invoice.CreatedAt);
    }
}
