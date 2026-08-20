using FluentAssertions;
using Moq;
using Forge.Api.Features.Invoices;
using Forge.Core.Entities;
using Forge.Core.Enums;
using Forge.Core.Interfaces;
using Forge.Data.Context;
using Forge.Tests.Helpers;

namespace Forge.Tests.Handlers.Invoices;

public class RenameInvoiceNumberTests
{
    private readonly Mock<IInvoiceRepository> _repo = new();
    private readonly Mock<ISystemSettingRepository> _systemSettings = new();
    private readonly Mock<IBusinessIdentifierService> _identifiers = new();
    private readonly AppDbContext _db = TestDbContextFactory.Create();
    private readonly RenameInvoiceNumberHandler _handler;

    public RenameInvoiceNumberTests()
    {
        _identifiers.Setup(i => i.IssueAsync(It.IsAny<BusinessEntityType>(), It.IsAny<int>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new BusinessIdentifier());
        _identifiers.Setup(i => i.RenameAsync(It.IsAny<BusinessEntityType>(), It.IsAny<int>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new BusinessIdentifier());

        _handler = new RenameInvoiceNumberHandler(
            _repo.Object, _systemSettings.Object, _identifiers.Object, _db);
    }

    private void AllowManualNumbers(bool allowed) =>
        _systemSettings.Setup(s => s.FindByKeyAsync("invoices.allow_manual_numbers", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new SystemSetting { Key = "invoices.allow_manual_numbers", Value = allowed ? "true" : "false" });

    private void SetupRepo(Invoice invoice)
    {
        _repo.Setup(r => r.FindAsync(invoice.Id, It.IsAny<CancellationToken>())).ReturnsAsync(invoice);
        _repo.Setup(r => r.SaveChangesAsync(It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);
    }

    [Fact]
    public async Task Renames_a_draft_invoice_number_when_allowed_and_unique()
    {
        var invoice = new Invoice { Id = 1, InvoiceNumber = "INV-00001", Status = InvoiceStatus.Draft };
        SetupRepo(invoice);
        AllowManualNumbers(true);
        _repo.Setup(r => r.InvoiceNumberExistsAsync("ACME-9", 1, It.IsAny<CancellationToken>())).ReturnsAsync(false);

        await _handler.Handle(new RenameInvoiceNumberCommand(1, "ACME-9"), CancellationToken.None);

        invoice.InvoiceNumber.Should().Be("ACME-9");
        _identifiers.Verify(i => i.IssueAsync(BusinessEntityType.Invoice, 1, "INV-00001", It.IsAny<CancellationToken>()), Times.Once);
        _identifiers.Verify(i => i.RenameAsync(BusinessEntityType.Invoice, 1, "ACME-9", It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Rejects_a_duplicate_invoice_number()
    {
        var invoice = new Invoice { Id = 1, InvoiceNumber = "INV-00001", Status = InvoiceStatus.Draft };
        SetupRepo(invoice);
        AllowManualNumbers(true);
        _repo.Setup(r => r.InvoiceNumberExistsAsync("TAKEN", 1, It.IsAny<CancellationToken>())).ReturnsAsync(true);

        var act = () => _handler.Handle(new RenameInvoiceNumberCommand(1, "TAKEN"), CancellationToken.None);

        (await act.Should().ThrowAsync<InvalidOperationException>()).Which.Message.Should().Contain("already in use");
        invoice.InvoiceNumber.Should().Be("INV-00001");
    }

    [Fact]
    public async Task Rejects_a_rename_when_manual_numbers_disabled()
    {
        var invoice = new Invoice { Id = 1, InvoiceNumber = "INV-00001", Status = InvoiceStatus.Draft };
        SetupRepo(invoice);
        AllowManualNumbers(false);

        var act = () => _handler.Handle(new RenameInvoiceNumberCommand(1, "ACME-9"), CancellationToken.None);

        (await act.Should().ThrowAsync<InvalidOperationException>()).Which.Message.Should().Contain("disabled");
        invoice.InvoiceNumber.Should().Be("INV-00001");
    }

    [Fact]
    public async Task Rejects_a_rename_once_the_invoice_is_no_longer_draft()
    {
        var invoice = new Invoice { Id = 1, InvoiceNumber = "INV-00001", Status = InvoiceStatus.Sent };
        SetupRepo(invoice);
        AllowManualNumbers(true);
        _repo.Setup(r => r.InvoiceNumberExistsAsync(It.IsAny<string>(), It.IsAny<int?>(), It.IsAny<CancellationToken>())).ReturnsAsync(false);

        var act = () => _handler.Handle(new RenameInvoiceNumberCommand(1, "ACME-9"), CancellationToken.None);

        (await act.Should().ThrowAsync<InvalidOperationException>()).Which.Message.Should().Contain("Draft");
        invoice.InvoiceNumber.Should().Be("INV-00001");
        _identifiers.Verify(i => i.RenameAsync(It.IsAny<BusinessEntityType>(), It.IsAny<int>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }
}
