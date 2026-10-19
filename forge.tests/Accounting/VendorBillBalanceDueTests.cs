using FluentAssertions;

using Forge.Core.Entities;

namespace Forge.Tests.Accounting;

public class VendorBillBalanceDueTests
{
    private static VendorBill SubCentBill(params decimal[] payments)
    {
        var bill = new VendorBill
        {
            TaxAmount = 0.075m,
            Lines = [new VendorBillLine { Quantity = 1.5m, UnitPrice = 0.3334m }],
        };
        foreach (var amount in payments)
            bill.PaymentApplications.Add(new VendorPaymentApplication { Amount = amount });
        return bill;
    }

    [Fact]
    public void BalanceDue_UnpaidSubCentBill_IsTheTotalRoundedToTheCent()
    {
        var bill = SubCentBill();

        bill.Subtotal.Should().Be(0.5001m);
        bill.Total.Should().Be(0.58m);
        bill.BalanceDue.Should().Be(0.58m);
    }

    [Fact]
    public void BalanceDue_PaidAtTheRoundedTotal_IsZero()
        => SubCentBill(0.58m).BalanceDue.Should().Be(0m);

    [Fact]
    public void BalanceDue_LegacyBillPaidAtItsRawTotal_IsZero()
        => SubCentBill(0.5751m).BalanceDue.Should().Be(0m);

    [Fact]
    public void BalanceDue_PartlyPaid_IsTheRoundedRemainder()
        => SubCentBill(0.25m).BalanceDue.Should().Be(0.33m);
}
