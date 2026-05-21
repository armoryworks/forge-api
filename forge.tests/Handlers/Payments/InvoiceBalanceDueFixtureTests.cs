using FluentAssertions;
using Forge.Core.Entities;

namespace Forge.Tests.Handlers.Payments;

/// <summary>
/// F-027 canonical balance fixtures (E1–E9) from
/// docs/domain/f026-f027-payment-balance-dod.md.
///
/// Asserts the single canonical formula Invoice.BalanceDue =
/// quantize(Total) − Σpayments − Σcredits (credits = structural 0m until BE-4).
///
/// Scope notes (flagged to ORCH — see STATUS report):
///  • E1,E2,E3,E6,E7,E8 are fully assertable code-only.
///  • E4,E5 require an APPLIED CREDIT / write-off credit memo — that model is BE-4
///    (F-027's credits term is the structural 0m placeholder). Here we assert the
///    pre-credit balance (the computable part) and that the residual is NOT
///    silently forgiven (Ruling #7, no epsilon).
///  • E9's literal "−$50 line adjustment" needs a per-line discount field that does
///    not exist (adding it = migration, out of F-027 code-only scope). The
///    de-divergence it proves — exactly one balance formula, consumed by the payment
///    path — is asserted by CreatePaymentHandlerTests' anti-drift test.
/// </summary>
public class InvoiceBalanceDueFixtureTests
{
    private static Invoice BuildInvoice(decimal taxRate, (decimal qty, decimal price)[] lines, decimal[] payments)
    {
        var inv = new Invoice { InvoiceNumber = "INV-FIX", TaxRate = taxRate };
        var n = 1;
        foreach (var (qty, price) in lines)
            inv.Lines.Add(new InvoiceLine { Quantity = qty, UnitPrice = price, LineNumber = n++, Description = "fixture" });
        foreach (var amt in payments)
            inv.PaymentApplications.Add(new PaymentApplication { Amount = amt });
        return inv;
    }

    // E1 — open/unpaid: total 1000, no payments → 1000.00 (OPEN)
    [Fact]
    public void E1_Open_Unpaid()
    {
        var inv = BuildInvoice(0m, [(1m, 1000m)], []);
        inv.BalanceDue.Should().Be(1000.00m);
    }

    // E2 — partially paid: total 1000, payment 400 → 600.00
    [Fact]
    public void E2_PartiallyPaid()
    {
        var inv = BuildInvoice(0m, [(1m, 1000m)], [400m]);
        inv.BalanceDue.Should().Be(600.00m);
    }

    // E3 — paid in full (exact): total 1000, payments 400 + 600 → 0.00
    [Fact]
    public void E3_PaidInFull_Exact()
    {
        var inv = BuildInvoice(0m, [(1m, 1000m)], [400m, 600m]);
        inv.BalanceDue.Should().Be(0.00m);
    }

    // E6 — payment reversal adds back: reversal removes the application; no separate term.
    [Fact]
    public void E6_Reversal_AddsBack()
    {
        var inv = BuildInvoice(0m, [(1m, 1000m)], [1000m]);
        inv.BalanceDue.Should().Be(0.00m, "fully applied before reversal");

        // Reverse/unapply: the application no longer counts toward Σpayments.
        inv.PaymentApplications.Clear();
        inv.BalanceDue.Should().Be(1000.00m, "a reversed application adds back to balance");
    }

    // E7 — overpayment never goes negative (interim, BE-4 unshipped):
    // only the remaining balance (1000) may be applied; balance hits 0, never −50.
    [Fact]
    public void E7_Overpayment_NeverNegative_InterimMaxApplied()
    {
        var inv = BuildInvoice(0m, [(1m, 1000m)], [1000m]); // the $50 excess is hard-blocked at the application layer (F-026)
        inv.BalanceDue.Should().Be(0.00m);
        inv.BalanceDue.Should().BeGreaterThanOrEqualTo(0m, "balance is never negative; excess lands as a customer credit (BE-4), not −50");
    }

    // E8 — no-epsilon boundary: total 1000, payment 999.99 → 0.01 (NOT auto-forgiven)
    [Fact]
    public void E8_NoEpsilon_Boundary()
    {
        var inv = BuildInvoice(0m, [(1m, 1000m)], [999.99m]);
        inv.BalanceDue.Should().Be(0.01m, "the residual cent is collected or explicitly written off, never silently swallowed (Ruling #7)");
    }

    // E4 — payment + credit memo (credit term is BE-4): pre-credit balance is computable.
    // Full E4 (balance 0 after a $300 short-ship credit) activates when BE-4 ships.
    [Fact]
    public void E4_PaymentPlusCredit_PreCreditBalance_DeferredToBE4()
    {
        var inv = BuildInvoice(0m, [(1m, 1000m)], [700m]);
        inv.BalanceDue.Should().Be(300.00m,
            "credits term is the structural 0m placeholder; the $300 credit applies under BE-4 to reach 0");
    }

    // E5 — write-off closes residual (write-off is a credit memo, BE-4): residual is NOT forgiven.
    [Fact]
    public void E5_WriteOffResidual_NotSilentlyForgiven_DeferredToBE4()
    {
        var inv = BuildInvoice(0m, [(1m, 1000m)], [997m]);
        inv.BalanceDue.Should().Be(3.00m,
            "the $3 residual remains as balance (no epsilon); it is closed via an explicit write-off credit under BE-4, not auto-zeroed");
    }

    // E9 — de-divergence: with tax, the canonical balance is the tax-inclusive quantized
    // figure, distinct from a naive Σ(Qty×UnitPrice) line sum. The literal −$50 line
    // adjustment needs a discount field (out of code-only scope); the single-formula
    // guarantee is asserted by CreatePaymentHandlerTests (payment path reads BalanceDue).
    [Fact]
    public void E9_CanonicalIsTaxInclusiveQuantized_SingleFormula()
    {
        var inv = BuildInvoice(0.08m, [(1m, 1000m)], []);
        var naiveLineSum = 1000m; // the retired Qty×UnitPrice view
        inv.BalanceDue.Should().Be(1080.00m);
        inv.BalanceDue.Should().NotBe(naiveLineSum, "canonical balance is tax-inclusive and quantized, not the naive line sum");
    }
}
