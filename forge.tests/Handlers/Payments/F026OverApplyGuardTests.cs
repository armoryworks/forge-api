using FluentAssertions;
using Forge.Api.Features.Payments;
using Forge.Core.Entities;
using Forge.Core.Enums;
using Forge.Data.Context;
using Forge.Data.Repositories;
using Forge.Tests.Helpers;

namespace Forge.Tests.Handlers.Payments;

/// <summary>
/// F-026 regression: payment application must be ≤ remaining balance;
/// invoice Version is bumped on each application so concurrent writes collide.
/// NOTE: the concurrent-collision path (A4/A5) requires a real Postgres integration
/// test (DbUpdateConcurrencyException is not enforced by the InMemory provider).
/// </summary>
public class F026OverApplyGuardTests : IDisposable
{
    private readonly AppDbContext _db;
    private readonly CreatePaymentHandler _handler;

    public F026OverApplyGuardTests()
    {
        _db = TestDbContextFactory.Create();
        var paymentRepo = new PaymentRepository(_db);
        var customerRepo = new CustomerRepository(_db);
        var invoiceRepo = new InvoiceRepository(_db);
        _handler = new CreatePaymentHandler(paymentRepo, customerRepo, invoiceRepo, _db);
    }

    private async Task<(Customer customer, Invoice invoice)> SeedAsync(decimal lineTotal)
    {
        var customer = new Customer { Name = "Test Co" };
        _db.Customers.Add(customer);
        await _db.SaveChangesAsync();

        var invoice = new Invoice
        {
            InvoiceNumber = $"INV-{Random.Shared.Next(10000, 99999)}",
            CustomerId = customer.Id,
            TaxRate = 0m,
            Status = InvoiceStatus.Sent,
        };
        invoice.Lines.Add(new InvoiceLine
        {
            Quantity = 1,
            UnitPrice = lineTotal,
            Description = "Test item",
            LineNumber = 1,
        });
        _db.Invoices.Add(invoice);
        await _db.SaveChangesAsync();

        return (customer, invoice);
    }

    // ── (a) Version bump ─────────────────────────────────────────────────────

    [Fact]
    public async Task Handle_PartialApplication_BumpsInvoiceVersion_F026()
    {
        var (customer, invoice) = await SeedAsync(lineTotal: 500m);
        var versionBefore = invoice.Version;

        await _handler.Handle(new CreatePaymentCommand(
            CustomerId: customer.Id,
            Method: "Check",
            Amount: 200m,
            PaymentDate: DateTimeOffset.UtcNow,
            ReferenceNumber: null,
            Notes: null,
            Applications: [new(invoice.Id, 200m)]), CancellationToken.None);

        await _db.Entry(invoice).ReloadAsync();
        invoice.Version.Should().BeGreaterThan(versionBefore,
            "each application must bump Version so concurrent writers collide on the token");
    }

    // ── (b) Over-apply guard ─────────────────────────────────────────────────

    [Fact]
    public async Task Handle_ApplicationExceedsBalance_ThrowsInvalidOperation_F026()
    {
        var (customer, invoice) = await SeedAsync(lineTotal: 100m);

        var act = () => _handler.Handle(new CreatePaymentCommand(
            CustomerId: customer.Id,
            Method: "Check",
            Amount: 150m,
            PaymentDate: DateTimeOffset.UtcNow,
            ReferenceNumber: null,
            Notes: null,
            Applications: [new(invoice.Id, 150m)]), CancellationToken.None);

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*exceeds*balance*");
    }

    [Fact]
    public async Task Handle_ApplicationExactlyAtBalance_Succeeds_F026()
    {
        var (customer, invoice) = await SeedAsync(lineTotal: 100m);

        var act = () => _handler.Handle(new CreatePaymentCommand(
            CustomerId: customer.Id,
            Method: "Check",
            Amount: 100m,
            PaymentDate: DateTimeOffset.UtcNow,
            ReferenceNumber: null,
            Notes: null,
            Applications: [new(invoice.Id, 100m)]), CancellationToken.None);

        await act.Should().NotThrowAsync();

        await _db.Entry(invoice).ReloadAsync();
        invoice.Status.Should().Be(InvoiceStatus.Paid);
    }

    public void Dispose() => _db.Dispose();
}
