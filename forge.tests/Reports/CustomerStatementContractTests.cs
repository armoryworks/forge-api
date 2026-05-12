using FluentAssertions;
using QuestPDF.Infrastructure;

using Forge.Api.Features.Customers;
using Forge.Core.Entities;
using Forge.Core.Enums;
using Forge.Data.Context;
using Forge.Tests.Helpers;

namespace Forge.Tests.Reports;

/// <summary>
/// WU-15 / RPT-CUSTSTMT-001 — contract test for /customers/{id}/statement.
///
/// Phase 1 found this endpoint returned 500 for every customer with invoices
/// because the QuestPDF table column widths (520pt fixed) overflowed the
/// Letter-page content area (~512pt) and threw DocumentLayoutException.
/// Fix: tighten fixed columns. This test exercises the PDF generator with a
/// representative invoice and asserts a non-empty byte[] is produced (i.e.
/// the layout no longer throws).
///
/// Does NOT subclass <see cref="ReportContractTestBase"/> because the
/// statement returns a byte stream (PDF) rather than an enumerable row set;
/// uses the same "contract test" shape.
/// </summary>
public class CustomerStatementContractTests
{
    static CustomerStatementContractTests()
    {
        // QuestPDF requires an explicit license declaration in test contexts.
        QuestPDF.Settings.License = LicenseType.Community;
    }

    [Fact]
    public async Task Statement_generator_produces_non_empty_pdf_with_seeded_invoices()
    {
        // Arrange — seeded customer + invoice + line + payment-application
        // shape mirrors what the production handler loads for a real customer.
        using var db = TestDbContextFactory.Create();
        var customer = new Customer
        {
            Name = "Statement Contract Co",
            Email = "billing@stmt.example",
            Phone = "+1-555-0100",
            IsActive = true,
        };
        db.Customers.Add(customer);
        await db.SaveChangesAsync();

        var invoice = new Invoice
        {
            InvoiceNumber = "INV-CT-0001",
            CustomerId = customer.Id,
            Status = InvoiceStatus.Sent,
            InvoiceDate = DateTimeOffset.UtcNow.AddDays(-30),
            DueDate = DateTimeOffset.UtcNow,
            TaxRate = 0.0875m,
        };
        db.Invoices.Add(invoice);
        await db.SaveChangesAsync();

        invoice.Lines.Add(new InvoiceLine
        {
            InvoiceId = invoice.Id,
            LineNumber = 1,
            Description = "Engineering services",
            Quantity = 10,
            UnitPrice = 150m,
        });
        await db.SaveChangesAsync();

        var payment = new Payment
        {
            PaymentNumber = "PMT-CT-0001",
            CustomerId = customer.Id,
            Amount = 250m,
            PaymentDate = DateTimeOffset.UtcNow.AddDays(-7),
            Method = PaymentMethod.Check,
        };
        db.Payments.Add(payment);
        await db.SaveChangesAsync();

        db.PaymentApplications.Add(new PaymentApplication
        {
            InvoiceId = invoice.Id,
            PaymentId = payment.Id,
            Amount = 250m,
        });
        await db.SaveChangesAsync();

        // Act — invoke the handler exactly as the controller does.
        var handler = new GenerateCustomerStatementHandler(db);
        var pdf = await handler.Handle(
            new GenerateCustomerStatementQuery(customer.Id),
            CancellationToken.None);

        // Assert — non-empty PDF (layout did not throw).
        pdf.Should().NotBeNull(
            "/customers/{id}/statement must return a PDF byte array");
        pdf.Length.Should().BeGreaterThan(0,
            "/customers/{id}/statement must produce a non-empty PDF — Phase 1 " +
            "saw 500 here from QuestPDF DocumentLayoutException (column widths " +
            "exceeded Letter-page content area)");
        // Verify PDF magic bytes.
        System.Text.Encoding.ASCII.GetString(pdf, 0, 4)
            .Should().Be("%PDF", "output must be a valid PDF document");
    }
}
