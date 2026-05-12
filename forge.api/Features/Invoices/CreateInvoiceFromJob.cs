using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;

using Forge.Core.Entities;
using Forge.Core.Interfaces;
using Forge.Core.Models;
using Forge.Data.Context;

namespace Forge.Api.Features.Invoices;

public record CreateInvoiceFromJobCommand(int JobId) : IRequest<InvoiceListItemModel>;

public class CreateInvoiceFromJobValidator : AbstractValidator<CreateInvoiceFromJobCommand>
{
    public CreateInvoiceFromJobValidator()
    {
        RuleFor(x => x.JobId).GreaterThan(0);
    }
}

public class CreateInvoiceFromJobHandler(
    AppDbContext db,
    IInvoiceRepository invoiceRepo)
    : IRequestHandler<CreateInvoiceFromJobCommand, InvoiceListItemModel>
{
    public async Task<InvoiceListItemModel> Handle(
        CreateInvoiceFromJobCommand request, CancellationToken cancellationToken)
    {
        var job = await db.Jobs
            .Include(j => j.Customer)
            .FirstOrDefaultAsync(j => j.Id == request.JobId, cancellationToken)
            ?? throw new KeyNotFoundException($"Job {request.JobId} not found");

        if (job.CompletedDate == null)
            throw new InvalidOperationException("Cannot create an invoice for a job that is not completed");

        if (job.CustomerId == null)
            throw new InvalidOperationException("Cannot create an invoice for a job without a customer");

        var customerName = job.Customer?.Name ?? "Unknown";
        var invoiceNumber = await invoiceRepo.GenerateNextInvoiceNumberAsync(cancellationToken);
        var now = DateTimeOffset.UtcNow;

        var invoice = new Invoice
        {
            InvoiceNumber = invoiceNumber,
            CustomerId = job.CustomerId.Value,
            InvoiceDate = now,
            DueDate = now.AddDays(30),
            Notes = $"Invoice for {job.JobNumber}: {job.Title}",
        };

        var description = string.IsNullOrWhiteSpace(job.Description)
            ? $"{job.JobNumber} — {job.Title}"
            : $"{job.JobNumber} — {job.Title}: {job.Description}";

        invoice.Lines.Add(new InvoiceLine
        {
            Description = description,
            Quantity = 1,
            UnitPrice = 0, // User sets price after creation
            LineNumber = 1,
        });

        await invoiceRepo.AddAsync(invoice, cancellationToken);
        await invoiceRepo.SaveChangesAsync(cancellationToken);

        var total = invoice.Lines.Sum(l => l.Quantity * l.UnitPrice) * (1 + invoice.TaxRate);

        return new InvoiceListItemModel(
            invoice.Id,
            invoice.InvoiceNumber,
            invoice.CustomerId,
            customerName,
            invoice.Status.ToString(),
            invoice.InvoiceDate,
            invoice.DueDate,
            total,
            0,
            total,
            invoice.CreatedAt);
    }
}
