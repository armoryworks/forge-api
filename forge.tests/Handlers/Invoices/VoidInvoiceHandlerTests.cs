using FluentAssertions;
using Moq;

using Forge.Api.Features.Invoices;
using Forge.Core.Entities;
using Forge.Core.Enums;
using Forge.Core.Interfaces;

namespace Forge.Tests.Handlers.Invoices;

// F-033: source-state whitelist for VoidInvoice
// Whitelist per AUDIT.md: {Sent, PartiallyPaid, Overdue}
// Draft invoices must be deleted, not voided.
// PartiallyPaid is in whitelist; payment guard below still blocks real paid cases.
public class VoidInvoiceHandlerTests
{
    private readonly Mock<IInvoiceRepository> _repo = new();
    private readonly VoidInvoiceHandler _handler;

    public VoidInvoiceHandlerTests()
    {
        _handler = new VoidInvoiceHandler(_repo.Object);
    }

    private Invoice InvoiceInStatus(InvoiceStatus status) => new()
    {
        Id = 1,
        Status = status,
        PaymentApplications = [],
    };

    // ── happy path: whitelist states with no payment applications ─────────────

    [Theory]
    [InlineData(InvoiceStatus.Sent)]
    [InlineData(InvoiceStatus.PartiallyPaid)]  // allowed — payment guard is the next layer
    [InlineData(InvoiceStatus.Overdue)]
    public async Task Handle_VoidableStatus_NoPayments_TransitionsToVoided(InvoiceStatus status)
    {
        var invoice = InvoiceInStatus(status);
        _repo.Setup(r => r.FindWithDetailsAsync(1, It.IsAny<CancellationToken>())).ReturnsAsync(invoice);

        await _handler.Handle(new VoidInvoiceCommand(1), CancellationToken.None);

        invoice.Status.Should().Be(InvoiceStatus.Voided);
        _repo.Verify(r => r.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    // ── F-033: source-state whitelist blocks ──────────────────────────────────

    [Theory]
    [InlineData(InvoiceStatus.Draft)]   // must be deleted, not voided
    [InlineData(InvoiceStatus.Paid)]
    [InlineData(InvoiceStatus.Voided)]  // re-void is a silent duplicate — blocked
    public async Task Handle_BlockedStatus_ThrowsInvalidOperation(InvoiceStatus status)
    {
        _repo.Setup(r => r.FindWithDetailsAsync(1, It.IsAny<CancellationToken>()))
            .ReturnsAsync(InvoiceInStatus(status));

        var act = () => _handler.Handle(new VoidInvoiceCommand(1), CancellationToken.None);

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*Allowed: Sent, PartiallyPaid, Overdue*");
        _repo.Verify(r => r.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    // ── existing payment guard still holds ───────────────────────────────────

    [Fact]
    public async Task Handle_SentWithPaymentApplications_ThrowsInvalidOperation()
    {
        var invoice = new Invoice
        {
            Id = 1,
            Status = InvoiceStatus.Sent,
            PaymentApplications = [new PaymentApplication { Amount = 100m }],
        };
        _repo.Setup(r => r.FindWithDetailsAsync(1, It.IsAny<CancellationToken>())).ReturnsAsync(invoice);

        var act = () => _handler.Handle(new VoidInvoiceCommand(1), CancellationToken.None);

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*payments applied*");
    }

    [Fact]
    public async Task Handle_InvoiceNotFound_ThrowsKeyNotFound()
    {
        _repo.Setup(r => r.FindWithDetailsAsync(99, It.IsAny<CancellationToken>())).ReturnsAsync((Invoice?)null);

        var act = () => _handler.Handle(new VoidInvoiceCommand(99), CancellationToken.None);

        await act.Should().ThrowAsync<KeyNotFoundException>();
    }
}
