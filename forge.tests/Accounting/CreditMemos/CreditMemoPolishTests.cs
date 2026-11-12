using System.Net;

using AwesomeAssertions;
using FluentValidation;
using MediatR;
using Moq;

using Forge.Api.Features.CreditMemos;
using Forge.Core.Entities;
using Forge.Core.Enums;
using Forge.Core.Models;
using Forge.Tests.Helpers;
using static Forge.Tests.Accounting.CreditMemos.CreditMemoTestContext;

namespace Forge.Tests.Accounting.CreditMemos;

/// <summary>
/// A credit against an invoice line never exceeds what was invoiced (price and cumulative amount), a draft is the
/// only memo that can be edited, and the memo PDF prints in the requested or company language.
/// </summary>
public class CreditMemoPolishTests
{
    private static IMediator Mediator(CreditMemoTestContext ctx)
    {
        var mediator = new Mock<IMediator>();
        mediator.Setup(m => m.Send(It.IsAny<GetCreditMemoByIdQuery>(), It.IsAny<CancellationToken>()))
            .Returns((GetCreditMemoByIdQuery q, CancellationToken ct) => new GetCreditMemoByIdHandler(ctx.Db).Handle(q, ct));
        return mediator.Object;
    }

    private static Task<CreditMemoDetailResponseModel> CreateAsync(CreditMemoTestContext ctx, CreateCreditMemoRequestModel data)
        => new CreateCreditMemoHandler(ctx.Db, ctx.Clock, Mediator(ctx)).Handle(new CreateCreditMemoCommand(data), CancellationToken.None);

    private static Task<CreditMemoDetailResponseModel> IssueAsync(CreditMemoTestContext ctx, int id)
        => new IssueCreditMemoHandler(ctx.Db, ctx.Issuer(), Mediator(ctx)).Handle(new IssueCreditMemoCommand(id), CancellationToken.None);

    private static CreateCreditMemoRequestModel Linked(int customerId, int invoiceId, int lineId, decimal quantity, decimal unitPrice)
        => new(customerId, invoiceId, CreditMemoReasonType.Return, "Return", null,
            [new CreditMemoLineRequestModel(CreditMemoLineType.Item, null, lineId, null, quantity, unitPrice)]);

    [Fact]
    public async Task A_linked_line_priced_above_the_invoiced_unit_price_is_refused()
    {
        using var ctx = await CreditMemoTestContext.CreateAsync();
        var customer = await ctx.CustomerAsync();
        var invoice = await ctx.PostedInvoiceAsync(customer,
            [new InvoiceLine { Description = "Widget", Quantity = 4m, UnitPrice = 25m, LineNumber = 1 }]);
        var lineId = invoice.Lines.Single().Id;

        var create = () => CreateAsync(ctx, Linked(customer.Id, invoice.Id, lineId, 1m, 25.01m));

        (await create.Should().ThrowAsync<ValidationException>())
            .Which.Errors.Should().Contain(e => e.ErrorCode == CreditMemoRules.CreditMemoLineOverInvoicedCode);
    }

    [Fact]
    public async Task An_unlinked_goodwill_line_stays_free()
    {
        using var ctx = await CreditMemoTestContext.CreateAsync();
        var customer = await ctx.CustomerAsync();
        var invoice = await ctx.PostedInvoiceAsync(customer,
            [new InvoiceLine { Description = "Widget", Quantity = 1m, UnitPrice = 25m, LineNumber = 1 }]);

        var memo = await CreateAsync(ctx, new CreateCreditMemoRequestModel(customer.Id, invoice.Id, CreditMemoReasonType.Other,
            "Goodwill", null, [new CreditMemoLineRequestModel(CreditMemoLineType.Item, null, null, "Goodwill", 1m, 40m)]));

        memo.Total.Should().Be(40m);
    }

    [Fact]
    public async Task Memos_on_one_line_can_not_credit_more_than_its_extended_amount()
    {
        using var ctx = await CreditMemoTestContext.CreateAsync();
        var customer = await ctx.CustomerAsync();
        var invoice = await ctx.PostedInvoiceAsync(customer,
            [new InvoiceLine { Description = "Shim", Quantity = 3m, UnitPrice = 0.335m, LineNumber = 1 }]);
        var lineId = invoice.Lines.Single().Id;

        var first = await CreateAsync(ctx, Linked(customer.Id, invoice.Id, lineId, 1m, 0.335m));
        await IssueAsync(ctx, first.Id);
        var second = await CreateAsync(ctx, Linked(customer.Id, invoice.Id, lineId, 1m, 0.335m));
        await IssueAsync(ctx, second.Id);

        var third = await CreateAsync(ctx, Linked(customer.Id, invoice.Id, lineId, 1m, 0.335m));
        var issue = () => IssueAsync(ctx, third.Id);
        (await issue.Should().ThrowAsync<ValidationException>())
            .Which.Errors.Should().Contain(e => e.ErrorCode == CreditMemoRules.CreditMemoLineOverInvoicedCode);
    }

    [Fact]
    public async Task Editing_an_issued_memo_is_a_conflict()
    {
        using var ctx = await CreditMemoTestContext.CreateAsync();
        var customer = await ctx.CustomerAsync();
        var memo = await CreateAsync(ctx, new CreateCreditMemoRequestModel(customer.Id, null, CreditMemoReasonType.Other,
            "Adjustment", null, [new CreditMemoLineRequestModel(CreditMemoLineType.Item, null, null, "Adjustment", 1m, 10m)]));
        await IssueAsync(ctx, memo.Id);

        var update = () => new UpdateCreditMemoHandler(ctx.Db, Mediator(ctx)).Handle(new UpdateCreditMemoCommand(memo.Id,
            new UpdateCreditMemoRequestModel(CreditMemoReasonType.Other, "Adjustment", Today,
                [new CreditMemoLineRequestModel(CreditMemoLineType.Item, null, null, "Adjustment", 2m, 10m)])), CancellationToken.None);

        await update.Should().ThrowAsync<InvalidOperationException>();
    }

    [Fact]
    public async Task The_pdf_prints_in_spanish_when_asked_and_in_the_company_default_otherwise()
    {
        using var ctx = await CreditMemoTestContext.CreateAsync();
        var customer = await ctx.CustomerAsync();
        var memo = await CreateAsync(ctx, new CreateCreditMemoRequestModel(customer.Id, null, CreditMemoReasonType.Other,
            "Ajuste", null, [new CreditMemoLineRequestModel(CreditMemoLineType.Item, null, null, "Ajuste", 1m, 10m)]));
        var settings = new Mock<Forge.Core.Interfaces.ISystemSettingRepository>();
        var renderer = new FakeHtmlPdfRenderer();
        var handler = new GenerateCreditMemoPdfHandler(ctx.Db, settings.Object, renderer);
        async Task<string> Text(GenerateCreditMemoPdfQuery query)
        {
            var pdf = await handler.Handle(query, CancellationToken.None);
            pdf.Should().StartWith("%PDF-"u8.ToArray());
            return WebUtility.HtmlDecode(renderer.Documents[^1].Html);
        }

        var english = await Text(new GenerateCreditMemoPdfQuery(memo.Id));
        var spanish = await Text(new GenerateCreditMemoPdfQuery(memo.Id, "es-US"));

        english.Should().Contain("CREDIT MEMO").And.Contain("Total credit:").And.NotContain("translated-footer");
        spanish.Should().Contain("NOTA DE CRÉDITO").And.Contain("Crédito total:").And.NotContain("CREDIT MEMO");
        spanish.Should().Contain("<div class=\"translated-footer\">" + memo.CreditMemoNumber + " · Página <span class=\"page\"></span> de <span class=\"pages\"></span></div>");

        ctx.Db.SupportedLanguages.Add(new SupportedLanguage { Code = "es", Name = "Spanish", NativeName = "Español", IsDefault = true });
        await ctx.Db.SaveChangesAsync();
        (await Text(new GenerateCreditMemoPdfQuery(memo.Id))).Should().Contain("NOTA DE CRÉDITO");
        (await Text(new GenerateCreditMemoPdfQuery(memo.Id, "en"))).Should().Contain("CREDIT MEMO");
    }

    [Fact]
    public void The_pdf_encodes_every_value_a_user_typed_and_labels_the_footer_with_the_memo_number()
    {
        var memo = new CreditMemo
        {
            CreditMemoNumber = "CM-2001",
            Customer = new Customer { Name = "<img src=x onerror=alert(1)>" },
            Reason = "Damaged <b>box</b>",
            Notes = "</div><script>fetch('http://internal/')</script>",
            CreditDate = Today,
            Lines = [new CreditMemoLine { LineNumber = 1, Description = "Bracket <b>bold</b>", Quantity = 1m, UnitPrice = 5m }],
        };

        var html = CreditMemoPdfDocument.Build(memo, "Harbor <Gear> Works", Forge.Api.Services.MoneyFormat.For(null, "USD")).Html;

        html.Should().NotContain("<img").And.NotContain("<script").And.NotContain("<b>");
        html.Should().Contain("&lt;img src=x onerror=alert(1)&gt;").And.Contain("Bracket &lt;b&gt;bold&lt;/b&gt;")
            .And.Contain("Damaged &lt;b&gt;box&lt;/b&gt;").And.Contain("Harbor &lt;Gear&gt; Works");
        html.Should().Contain("<div class=\"page-footer\">CM-2001 &middot; Page ");
    }
}
