using FluentAssertions;
using Microsoft.EntityFrameworkCore;

using Forge.Api.Features.Accounting;
using Forge.Api.Features.CreditMemos;
using Forge.Core.Entities;
using Forge.Core.Enums;
using Forge.Core.Models;
using Forge.Data.Repositories;
using Forge.Data.Services;
using Forge.Tests.Helpers;

namespace Forge.Tests.Accounting.CreditMemos;

/// <summary>
/// The credit memo, refund and return columns against real Postgres: the mapping matches the schema and the queries
/// that subtract credits and refunds in SQL translate.
/// </summary>
[Collection(PostgresCollection.Name)]
public sealed class CreditMemoPostgresTests(PostgresFixture fixture)
{
    [Fact]
    public async Task Credits_and_refunds_round_trip_and_net_in_the_sql_projections()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        int customerId, invoiceId, paymentId;
        await using (var db = fixture.CreateContext())
        {
            var currency = await db.Currencies.IgnoreQueryFilters().FirstOrDefaultAsync(c => c.Code == "XTS");
            if (currency is null)
            {
                await db.Database.ExecuteSqlRawAsync(
                    "SELECT setval(pg_get_serial_sequence('currencies', 'id'), COALESCE(MAX(id), 0) + 1, false) FROM currencies");
                currency = new Currency { Code = "XTS", Name = "Testing currency", Symbol = "¤", DecimalPlaces = 2, IsActive = true };
                db.Currencies.Add(currency);
                await db.SaveChangesAsync();
            }
            var currencyId = currency.Id;
            var customer = new Customer { Name = $"Credit {suffix}" };
            db.Customers.Add(customer);
            await db.SaveChangesAsync();
            customerId = customer.Id;

            var invoice = new Invoice
            {
                InvoiceNumber = $"I{suffix}", CustomerId = customer.Id, CurrencyId = currencyId, Status = InvoiceStatus.PartiallyPaid,
                InvoiceDate = DateTimeOffset.UtcNow.AddDays(-5), DueDate = DateTimeOffset.UtcNow.AddDays(25), TaxRate = 0m,
                Lines = [new InvoiceLine { Description = "Widget", Quantity = 4m, UnitPrice = 25m, LineNumber = 1 }],
            };
            db.Invoices.Add(invoice);
            var payment = new Payment
            {
                PaymentNumber = $"P{suffix}", CustomerId = customer.Id, CurrencyId = currencyId, Method = PaymentMethod.Check,
                Amount = 70m, PaymentDate = DateTimeOffset.UtcNow,
            };
            db.Payments.Add(payment);
            await db.SaveChangesAsync();
            invoiceId = invoice.Id;
            paymentId = payment.Id;

            var memo = new CreditMemo
            {
                CreditMemoNumber = $"C{suffix}", CustomerId = customer.Id, CurrencyId = currencyId, InvoiceId = invoice.Id,
                ReasonType = CreditMemoReasonType.Return, Reason = "Damaged", Status = CreditMemoStatus.PartiallyApplied,
                CreditDate = DateTimeOffset.UtcNow,
                Lines = [new CreditMemoLine { LineNumber = 1, InvoiceLineId = invoice.Lines.Single().Id, Description = "Widget", Quantity = 2m, UnitPrice = 25m }],
            };
            db.CreditMemos.Add(memo);
            await db.SaveChangesAsync();
            db.CreditMemoApplications.Add(new CreditMemoApplication { CreditMemoId = memo.Id, InvoiceId = invoice.Id, Amount = 30m, AppliedAt = DateTimeOffset.UtcNow });
            db.CustomerRefunds.AddRange(
                new CustomerRefund
                {
                    RefundNumber = $"R{suffix}", CustomerId = customer.Id, CurrencyId = currencyId, Amount = 20m, Method = PaymentMethod.Check,
                    RefundDate = DateTimeOffset.UtcNow, PaymentId = payment.Id,
                },
                new CustomerRefund
                {
                    RefundNumber = $"S{suffix}", CustomerId = customer.Id, CurrencyId = currencyId, Amount = 5m, Method = PaymentMethod.Check,
                    RefundDate = DateTimeOffset.UtcNow, CreditMemoId = memo.Id,
                });
            db.CustomerReturns.Add(new CustomerReturn
            {
                ReturnNumber = $"RMA-{suffix}", CustomerId = customer.Id, Reason = "Damaged", ReturnDate = DateTimeOffset.UtcNow,
                InvoiceId = invoice.Id, InvoiceLineId = invoice.Lines.Single().Id, Disposition = ReturnDisposition.Scrap,
                QuantityScrapped = 2m, CreditMemoId = memo.Id,
            });
            await db.SaveChangesAsync();
        }

        await using (var db = fixture.CreateContext())
        {
            var page = await new InvoiceRepository(db).GetPagedAsync(new InvoiceListQuery { CustomerId = customerId, OpenOnly = true }, CancellationToken.None);
            page.Items.Single().BalanceDue.Should().Be(70m);

            var payments = await new PaymentRepository(db).GetPagedAsync(new PaymentListQuery { CustomerId = customerId }, CancellationToken.None);
            payments.Items.Single().UnappliedAmount.Should().Be(50m);

            var exposure = await CustomerCreditExposure.ForCustomerAsync(db, customerId, CancellationToken.None);
            exposure.Should().NotBeNull();

            var open = await new GetCreditMemosHandler(db).Handle(
                new GetCreditMemosQuery(customerId, null, invoiceId, OpenOnly: true, Search: "damaged"), CancellationToken.None);
            open.Single().OpenAmount.Should().Be(15m);

            var detail = await new GetCreditMemoByIdHandler(db).Handle(new GetCreditMemoByIdQuery(open.Single().Id), CancellationToken.None);
            detail.Applications.Single().Amount.Should().Be(30m);
            detail.Refunds.Single().Amount.Should().Be(5m);

            var aging = await new ReportRepository(db).GetArAgingAsync(CancellationToken.None);
            aging.Should().Contain(r => r.DocumentType == "CreditMemo" && r.InvoiceNumber == $"C{suffix}" && r.BalanceDue == -15m);

            var tax = await new GetSalesTaxLiabilityHandler(db).Handle(new GetSalesTaxLiabilityQuery(), CancellationToken.None);
            tax.Should().NotBeNull();

            var ret = await db.CustomerReturns.AsNoTracking().Include(r => r.CreditMemo).SingleAsync(r => r.ReturnNumber == $"RMA-{suffix}");
            ret.CreditMemo!.CreditMemoNumber.Should().Be($"C{suffix}");
            ret.Disposition.Should().Be(ReturnDisposition.Scrap);

            (await db.Payments.Include(p => p.Refunds).SingleAsync(p => p.Id == paymentId)).RefundedAmount.Should().Be(20m);
        }
    }
}
