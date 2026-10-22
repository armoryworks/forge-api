using FluentAssertions;
using FluentValidation;

using Forge.Api.Features.Estimates;
using Forge.Api.Features.Jobs;
using Forge.Api.Features.Parts;
using Forge.Api.Features.PurchaseOrders;
using Forge.Api.Features.Quotes;
using Forge.Api.Features.VendorBills;
using Forge.Api.Features.VendorPayments;
using Forge.Api.Validation;
using Forge.Core.Models;

namespace Forge.Tests.Handlers;

public class AmountPrecisionValidatorTests
{
    private static readonly DateTimeOffset BillDate = new(2026, 10, 1, 0, 0, 0, TimeSpan.Zero);

    public AmountPrecisionValidatorTests() => FluentValidationDefaults.Apply();

    private static string[] Errors<T>(IValidator<T> validator, T instance)
        => validator.Validate(instance).Errors.Select(e => e.ErrorMessage).ToArray();

    private static CreateVendorBillCommand Bill(decimal tax, decimal qty = 1.5m, decimal price = 0.3334m)
        => new(1, null, null, BillDate, BillDate, tax, null,
            [new CreateVendorBillLineModel(null, "Line", qty, price, "OPERATING_EXPENSE")]);

    [Fact]
    public void VendorBill_SubCentTax_IsRejected()
        => Errors(new CreateVendorBillValidator(), Bill(0.075m))
            .Should().ContainSingle(e => e == "Tax amount can have at most 2 decimal places.");

    [Fact]
    public void VendorBill_FourDecimalUnitPriceAndTwoDecimalTax_IsAccepted()
        => Errors(new CreateVendorBillValidator(), Bill(0.07m)).Should().BeEmpty();

    [Fact]
    public void VendorBill_FiveDecimalLine_IsRejected()
        => Errors(new CreateVendorBillValidator(), Bill(0m, qty: 1.00001m, price: 0.33335m))
            .Should().HaveCount(2).And.OnlyContain(e => e.EndsWith("at most 4 decimal places."));

    [Fact]
    public void VendorPayment_SubCentAmounts_AreRejected()
    {
        var command = new CreateVendorPaymentCommand(1, "Check", 0.5751m, BillDate, null, null,
            [new CreateVendorPaymentApplicationModel(1, 0.5751m)]);

        Errors(new CreateVendorPaymentValidator(), command)
            .Where(e => e.Contains("at most 2 decimal places")).Should().HaveCount(2);
    }

    [Fact]
    public void VendorPayment_CentAmounts_AreAccepted()
    {
        var command = new CreateVendorPaymentCommand(1, "Check", 0.58m, BillDate, null, null,
            [new CreateVendorPaymentApplicationModel(1, 0.58m)]);

        Errors(new CreateVendorPaymentValidator(), command).Should().BeEmpty();
    }

    [Theory]
    [InlineData(3.14159, 10)]
    [InlineData(1, 0.33335)]
    public void PurchaseOrderLine_MoreThanFourDecimals_IsRejected(decimal qty, decimal price)
    {
        var command = new CreatePurchaseOrderCommand(1, null, null,
            [new CreatePurchaseOrderLineModel(null, "Steel", qty, price, null)]);

        Errors(new CreatePurchaseOrderValidator(), command)
            .Should().ContainSingle(e => e.EndsWith("at most 4 decimal places."));
    }

    [Fact]
    public void QuoteLine_MoreThanFourDecimals_IsRejected()
        => Errors(new AddQuoteLineValidator(), new AddQuoteLineCommand(1, new CreateQuoteLineModel(null, "x", 1m, 0.12345m, null)))
            .Should().ContainSingle(e => e.EndsWith("Unit price can have at most 4 decimal places."));

    [Fact]
    public void QuoteLine_FourDecimalPrice_IsAccepted()
        => Errors(new AddQuoteLineValidator(), new AddQuoteLineCommand(1, new CreateQuoteLineModel(null, "x", 3m, 0.125m, null)))
            .Should().BeEmpty();

    [Fact]
    public void Estimate_SubCentAmount_IsRejected()
        => Errors(new CreateEstimateValidator(), new CreateEstimateCommand(1, "Job", null, 0.30000000000000004m, null, null, null))
            .Should().ContainSingle(e => e == "Estimated amount can have at most 2 decimal places.");

    [Fact]
    public void PartManualCostOverride_FourDecimals_IsAcceptedButFiveIsRejected()
    {
        static UpdatePartCommand Update(decimal cost) => new(1, new UpdatePartRequestModel(
            null, null, null, null, null, null, null, null, null, null, null, null, ManualCostOverride: cost));

        Errors(new UpdatePartCommandValidator(), Update(12.3456m)).Should().BeEmpty();
        Errors(new UpdatePartCommandValidator(), Update(12.34567m))
            .Should().ContainSingle(e => e.EndsWith("at most 4 decimal places."));
    }

    [Fact]
    public void ReviewPriceOverride_FiveDecimalEnteredPrice_IsRejected()
    {
        static ReviewPriceOverrideQuery Review(decimal price) => new(new ReviewPriceOverrideRequestModel(1, 2, 10m, null, price, null));

        Errors(new ReviewPriceOverrideQueryValidator(), Review(1.2345m)).Should().BeEmpty();
        Errors(new ReviewPriceOverrideQueryValidator(), Review(1.23456m))
            .Should().ContainSingle(e => e.EndsWith("at most 4 decimal places."));
    }

    [Fact]
    public void SendOutSubcontract_FiveDecimalUnitCost_IsRejectedOnTheUnitCostField()
    {
        var failures = new SendOutSubcontractValidator()
            .Validate(new SendOutSubcontractCommand(1, 1, new SendOutRequestModel(2m, 1.23456m, null, null, null, true)))
            .Errors;

        failures.Should().ContainSingle();
        failures[0].PropertyName.Should().Be("Data.UnitCost");
        failures[0].ErrorMessage.Should().EndWith("at most 4 decimal places.");
    }

    [Fact]
    public void SendOutSubcontract_FiveDecimalQuantity_IsRejectedOnTheQuantityField()
    {
        var failures = new SendOutSubcontractValidator()
            .Validate(new SendOutSubcontractCommand(1, 1, new SendOutRequestModel(1.00001m, 5m, null, null, null)))
            .Errors;

        failures.Should().ContainSingle().Which.PropertyName.Should().Be("Data.Quantity");
    }

    [Fact]
    public void SendOutSubcontract_FourDecimalsWithTrailingZeros_IsAccepted()
        => new SendOutSubcontractValidator()
            .Validate(new SendOutSubcontractCommand(1, 1, new SendOutRequestModel(2.500000m, 1.234500m, null, null, null, true)))
            .IsValid.Should().BeTrue();
}
