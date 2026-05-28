using FluentAssertions;
using Moq;

using Forge.Api.Features.Quotes;
using Forge.Core.Entities;
using Forge.Core.Enums;
using Forge.Core.Interfaces;

namespace Forge.Tests.Remediation.Quotes;

/// <summary>
/// TDD remediation tests for audit findings on the Quote → Sales-Order convert.
///
/// These encode the *definition-of-correct* behavior; they FAIL against today's
/// code (RED). Each is <c>[Fact(Skip = "RED: &lt;finding&gt;")]</c> so it documents
/// the contract without breaking the green CI gate — the burn-down step is
/// "remove the Skip + implement until it passes." See ../README.md.
///
/// Findings: AUDIT-S3 (Notes drop) and AUDIT-S4 / BE20-C (zero-line order).
/// Source: forge-api/forge.api/Features/Quotes/ConvertQuoteToOrder.cs (lines 27-48).
/// Note: the broader S3 "5 header fields dropped" is narrower than stated —
/// CreditTerms/BillingAddressId/RequestedDeliveryDate/CustomerPO are SalesOrder-only
/// (no Quote source), so the convert-bug is the Notes drop; the other four are a
/// UI gap (SO-edit dead, finding SO-8), tracked separately in BACKLOG.md.
/// </summary>
public class ConvertQuoteToOrderRemediationTests
{
    [Fact] // AUDIT-S3 — GREEN: ConvertQuoteToOrder now copies Notes onto the SalesOrder.
    public async Task ConvertQuoteToOrder_carries_quote_Notes_onto_the_order()
    {
        SalesOrder? captured = null;
        var quote = AcceptedQuoteWithOneLine();
        quote.Notes = "Rush — call before shipping";
        var handler = BuildHandler(quote, o => captured = o);

        await handler.Handle(new ConvertQuoteToOrderCommand(quote.Id), CancellationToken.None);

        captured.Should().NotBeNull();
        captured!.Notes.Should().Be("Rush — call before shipping",
            "converting a quote must preserve its Notes onto the order (definition-of-correct)");
    }

    [Fact] // AUDIT-S4 / BE20-C — GREEN: ConvertQuoteToOrder now rejects a zero-line quote.
    public async Task ConvertQuoteToOrder_rejects_a_quote_with_no_lines()
    {
        var quote = AcceptedQuoteWithOneLine();
        quote.Lines.Clear(); // zero-line quote (e.g. estimate-derived) must not convert

        var handler = BuildHandler(quote, _ => { });

        var act = () => handler.Handle(new ConvertQuoteToOrderCommand(quote.Id), CancellationToken.None);

        await act.Should().ThrowAsync<InvalidOperationException>(
            "an empty quote cannot become a live, confirmable sales order");
    }

    // ── helpers ───────────────────────────────────────────────────────────────
    private static Quote AcceptedQuoteWithOneLine() => new()
    {
        Id = 1,
        CustomerId = 7,
        Status = QuoteStatus.Accepted,
        SalesOrder = null,
        Customer = new Customer { Name = "Acme Co" },
        Lines = { new QuoteLine { PartId = 1, Description = "Widget", Quantity = 2, UnitPrice = 10m } },
    };

    private static ConvertQuoteToOrderHandler BuildHandler(Quote quote, Action<SalesOrder> onAdd)
    {
        var quoteRepo = new Mock<IQuoteRepository>();
        quoteRepo.Setup(r => r.FindWithDetailsAsync(quote.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(quote);
        quoteRepo.Setup(r => r.SaveChangesAsync(It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var orderRepo = new Mock<ISalesOrderRepository>();
        orderRepo.Setup(r => r.GenerateNextOrderNumberAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync("SO-1");
        orderRepo.Setup(r => r.AddAsync(It.IsAny<SalesOrder>(), It.IsAny<CancellationToken>()))
            .Callback<SalesOrder, CancellationToken>((o, _) => onAdd(o))
            .Returns(Task.CompletedTask);

        return new ConvertQuoteToOrderHandler(quoteRepo.Object, orderRepo.Object);
    }
}
