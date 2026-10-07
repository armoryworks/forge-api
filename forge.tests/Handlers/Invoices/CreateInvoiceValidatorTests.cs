using FluentAssertions;

using Forge.Api.Features.Invoices;
using Forge.Core.Models;

namespace Forge.Tests.Handlers.Invoices;

public class CreateInvoiceValidatorTests
{
    private static readonly DateTimeOffset InvoiceDate = new(2026, 10, 1, 0, 0, 0, TimeSpan.Zero);

    private static CreateInvoiceCommand Command(string? invoiceNumber) =>
        new(1, null, null, InvoiceDate, InvoiceDate.AddDays(30), null, 0m, null,
            [new CreateInvoiceLineModel(null, "Freight", 1m, 25m)],
            InvoiceNumber: invoiceNumber);

    [Fact]
    public void Rejects_an_invoice_number_longer_than_the_column()
    {
        var result = new CreateInvoiceValidator().Validate(Command(new string('A', 25)));

        result.IsValid.Should().BeFalse();
        result.Errors.Should().ContainSingle(e => e.PropertyName == nameof(CreateInvoiceCommand.InvoiceNumber));
    }

    [Fact]
    public void Accepts_a_twenty_character_invoice_number()
    {
        new CreateInvoiceValidator().Validate(Command(new string('A', 20)))
            .IsValid.Should().BeTrue();
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Accepts_a_blank_invoice_number(string? invoiceNumber)
    {
        new CreateInvoiceValidator().Validate(Command(invoiceNumber))
            .IsValid.Should().BeTrue();
    }
}
