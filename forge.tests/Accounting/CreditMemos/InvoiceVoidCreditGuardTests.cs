using FluentAssertions;
using FluentValidation;
using MediatR;
using Moq;

using Forge.Api.Features.CreditMemos;
using Forge.Api.Features.CustomerReturns;
using Forge.Api.Features.Invoices;
using Forge.Core.Entities;
using Forge.Core.Enums;
using Forge.Core.Models;
using Forge.Data.Repositories;
using static Forge.Tests.Accounting.CreditMemos.CreditMemoTestContext;

namespace Forge.Tests.Accounting.CreditMemos;

/// <summary>
/// Voiding an invoice reverses its revenue, tax and cost of goods in full, so it is refused while an issued credit memo
/// or a restocked or scrapped return already reverses part of the same sale.
/// </summary>
public class InvoiceVoidCreditGuardTests
{
    private static IMediator Mediator(CreditMemoTestContext ctx)
    {
        var mediator = new Mock<IMediator>();
        mediator.Setup(m => m.Send(It.IsAny<GetCreditMemoByIdQuery>(), It.IsAny<CancellationToken>()))
            .Returns((GetCreditMemoByIdQuery q, CancellationToken ct) => new GetCreditMemoByIdHandler(ctx.Db).Handle(q, ct));
        return mediator.Object;
    }

    private static Func<Task> VoidInvoice(CreditMemoTestContext ctx, int invoiceId)
        => () => new VoidInvoiceHandler(new InvoiceRepository(ctx.Db), ctx.InvoicePosting(), null, ctx.Db, ctx.Clock)
            .Handle(new VoidInvoiceCommand(invoiceId), CancellationToken.None);

    [Fact]
    public async Task An_invoice_with_an_issued_linked_credit_memo_cannot_be_voided_until_the_memo_is()
    {
        using var ctx = await CreditMemoTestContext.CreateAsync();
        var customer = await ctx.CustomerAsync();
        var invoice = await ctx.PostedInvoiceAsync(customer,
            [new InvoiceLine { Description = "Widget", Quantity = 2m, UnitPrice = 150m, LineNumber = 1 }]);
        var memo = await new CreateCreditMemoHandler(ctx.Db, ctx.Clock, Mediator(ctx)).Handle(new CreateCreditMemoCommand(
            new CreateCreditMemoRequestModel(customer.Id, invoice.Id, CreditMemoReasonType.Return, "Damaged", null,
                [new CreditMemoLineRequestModel(CreditMemoLineType.Item, null, invoice.Lines.Single().Id, null, 1m, 150m)])),
            CancellationToken.None);
        memo = await new IssueCreditMemoHandler(ctx.Db, ctx.Issuer(), Mediator(ctx))
            .Handle(new IssueCreditMemoCommand(memo.Id), CancellationToken.None);
        memo = await new UnapplyCreditMemoHandler(ctx.Db, ctx.Posting(), ctx.Clock, Mediator(ctx))
            .Handle(new UnapplyCreditMemoCommand(memo.Id, memo.Applications.Single().Id), CancellationToken.None);

        (await VoidInvoice(ctx, invoice.Id).Should().ThrowAsync<ValidationException>())
            .Which.Errors.Should().Contain(e => e.ErrorCode == CreditMemoRules.InvoiceHasCreditsCode);

        await new VoidCreditMemoHandler(ctx.Db, ctx.Posting(), ctx.Clock, Mediator(ctx))
            .Handle(new VoidCreditMemoCommand(memo.Id, "Keyed in error"), CancellationToken.None);
        ctx.Db.ChangeTracker.Clear();

        await VoidInvoice(ctx, invoice.Id).Should().NotThrowAsync();
        (await ctx.NetByAccountAsync()).GetValueOrDefault(ArId).Should().Be(0m);
    }

    [Theory]
    [InlineData(ReturnDisposition.ReturnToStock)]
    [InlineData(ReturnDisposition.Scrap)]
    public async Task An_invoice_whose_returned_goods_were_restocked_or_scrapped_cannot_be_voided(ReturnDisposition disposition)
    {
        using var ctx = await CreditMemoTestContext.CreateAsync();
        var customer = await ctx.CustomerAsync();
        var part = await ctx.FinishedGoodAsync();
        var invoice = await ctx.PostedInvoiceAsync(customer,
            [new InvoiceLine { PartId = part.Id, Description = "Widget", Quantity = 5m, UnitPrice = 189m, LineNumber = 1 }]);
        var ret = new CustomerReturn
        {
            ReturnNumber = "RMA-00001", CustomerId = customer.Id, Reason = "Wrong colour", ReturnDate = Today,
            Quantity = 2m, InvoiceId = invoice.Id, InvoiceLineId = invoice.Lines.Single().Id,
        };
        ctx.Db.CustomerReturns.Add(ret);
        await ctx.Db.SaveChangesAsync();
        var location = await ctx.LocationAsync();

        await ctx.ResolveHandler().Handle(new ResolveCustomerReturnCommand(
            ret.Id, null, disposition, 2m, disposition == ReturnDisposition.ReturnToStock ? location.Id : null, IssueCredit: false),
            CancellationToken.None);
        ctx.Db.ChangeTracker.Clear();

        (await VoidInvoice(ctx, invoice.Id).Should().ThrowAsync<ValidationException>())
            .Which.Errors.Should().Contain(e => e.ErrorCode == ReturnResolveCodes.InvoiceHasDisposedReturn);
    }
}
