using FluentAssertions;
using MediatR;
using Microsoft.AspNetCore.Http;
using Moq;

using Forge.Api.Features.Estimates;
using Forge.Api.Features.SalesOrders;
using Forge.Api.Features.SalesTax;
using Forge.Api.Services;
using Forge.Core.Entities;
using Forge.Core.Enums;
using Forge.Core.Interfaces;
using Forge.Core.Models;
using Forge.Data.Context;
using Forge.Tests.Helpers;

namespace Forge.Tests.Handlers.SalesOrders;

public class SalesOrderTaxGuardTests
{
    private const int CustomerId = 2;
    private const decimal DefaultRate = 0.0725m;
    private static readonly DateTimeOffset Now = new(2026, 7, 7, 12, 0, 0, TimeSpan.Zero);

    private readonly AppDbContext _db = TestDbContextFactory.Create();
    private readonly Mock<IMediator> _mediator = new();
    private readonly Mock<IClock> _clock = new();

    public SalesOrderTaxGuardTests()
    {
        _clock.SetupGet(c => c.UtcNow).Returns(Now);
        _mediator
            .Setup(m => m.Send(It.IsAny<GetTaxRateForCustomerQuery>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new SalesTaxRateResponseModel(
                1, "CA State", "CA", "CA", DefaultRate, Now.AddYears(-1), null, false, true, null));
    }

    private TaxOverrideGuard Guard() => new(_db, _mediator.Object, _clock.Object);

    private async Task SeedTaxDocumentAsync(TaxDocumentStatus status, DateTimeOffset? expires)
    {
        _db.CustomerTaxDocuments.Add(new CustomerTaxDocument
        {
            Id = 5,
            CustomerId = CustomerId,
            FileAttachmentId = 10,
            StateCode = "CA",
            CertificateType = "Resale",
            Status = status,
            VerifiedById = 7,
            VerifiedAt = Now.AddDays(-1),
            ExpirationDate = expires,
        });
        await _db.SaveChangesAsync();
    }

    private IQueryable<AuditLogEntry> OverrideAudits() =>
        _db.AuditLogEntries.Where(a => a.Action == "quote.tax_override");

    private CreateSalesOrderHandler CreateHandler(Action<SalesOrder> onAdd)
    {
        var repo = new Mock<ISalesOrderRepository>();
        repo.Setup(r => r.GenerateNextOrderNumberAsync(It.IsAny<CancellationToken>())).ReturnsAsync("SO-00001");
        repo.Setup(r => r.AddAsync(It.IsAny<SalesOrder>(), It.IsAny<CancellationToken>()))
            .Callback<SalesOrder, CancellationToken>((o, _) => onAdd(o))
            .Returns(Task.CompletedTask);
        repo.Setup(r => r.SaveChangesAsync(It.IsAny<CancellationToken>()))
            .Returns((CancellationToken ct) => _db.SaveChangesAsync(ct));

        var customers = new Mock<ICustomerRepository>();
        customers.Setup(r => r.FindAsync(CustomerId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Customer { Id = CustomerId, Name = "Tax Co" });

        var addresses = new Mock<ICustomerAddressRepository>();
        addresses.Setup(r => r.GetByCustomerAsync(It.IsAny<int>(), It.IsAny<CancellationToken>(), It.IsAny<bool>()))
            .ReturnsAsync([]);

        return new CreateSalesOrderHandler(
            repo.Object, customers.Object, Mock.Of<IPartRepository>(), Mock.Of<IBarcodeService>(),
            addresses.Object, taxGuard: Guard());
    }

    private static CreateSalesOrderCommand CreateCommand(decimal taxRate) =>
        new(CustomerId, null, null, null, null, null, null, null, taxRate,
            [new CreateSalesOrderLineModel(null, "Widget", 1, 10m, null)]);

    private (UpdateSalesOrderHandler Handler, SalesOrder Order) UpdateHandler(decimal currentRate)
    {
        var order = new SalesOrder
        {
            Id = 31, OrderNumber = "SO-00031", CustomerId = CustomerId,
            Status = SalesOrderStatus.Draft, TaxRate = currentRate,
        };
        var repo = new Mock<ISalesOrderRepository>();
        repo.Setup(r => r.FindAsync(order.Id, It.IsAny<CancellationToken>())).ReturnsAsync(order);
        repo.Setup(r => r.SaveChangesAsync(It.IsAny<CancellationToken>()))
            .Returns((CancellationToken ct) => _db.SaveChangesAsync(ct));

        var handler = new UpdateSalesOrderHandler(
            repo.Object,
            Mock.Of<ISystemSettingRepository>(),
            Mock.Of<IBusinessIdentifierService>(),
            _db,
            Mock.Of<IMediator>(),
            Mock.Of<IHttpContextAccessor>(),
            Guard());
        return (handler, order);
    }

    private static UpdateSalesOrderCommand UpdateTaxRate(int id, decimal rate) =>
        new(id, null, null, null, null, null, null, rate);

    [Fact]
    public async Task Create_with_the_customer_default_rate_passes_without_an_override_audit()
    {
        SalesOrder? added = null;

        await CreateHandler(o => added = o).Handle(CreateCommand(DefaultRate), CancellationToken.None);

        added.Should().NotBeNull();
        added!.TaxRate.Should().Be(DefaultRate);
        OverrideAudits().Should().BeEmpty();
    }

    [Fact]
    public async Task Create_with_a_different_rate_and_no_certificate_is_refused()
    {
        SalesOrder? added = null;

        var act = () => CreateHandler(o => added = o).Handle(CreateCommand(0m), CancellationToken.None);

        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("*verified state tax certificate*");
        added.Should().BeNull("a refused override must not create the order");
    }

    [Fact]
    public async Task Create_with_a_different_rate_and_an_expired_certificate_is_refused()
    {
        await SeedTaxDocumentAsync(TaxDocumentStatus.Verified, Now.AddDays(-1));

        var act = () => CreateHandler(_ => { }).Handle(CreateCommand(0m), CancellationToken.None);

        await act.Should().ThrowAsync<InvalidOperationException>();
    }

    [Fact]
    public async Task Create_with_a_different_rate_and_a_verified_certificate_passes_and_audits()
    {
        await SeedTaxDocumentAsync(TaxDocumentStatus.Verified, Now.AddYears(1));
        SalesOrder? added = null;

        await CreateHandler(o => added = o).Handle(CreateCommand(0m), CancellationToken.None);

        added!.TaxRate.Should().Be(0m);
        var audit = OverrideAudits().Should().ContainSingle().Subject;
        audit.EntityType.Should().Be("Customer");
        audit.EntityId.Should().Be(CustomerId);
        audit.Details.Should().Contain("\"taxDocumentId\":5");
    }

    [Fact]
    public async Task Update_to_a_different_rate_without_a_certificate_is_refused()
    {
        var (handler, order) = UpdateHandler(DefaultRate);

        var act = () => handler.Handle(UpdateTaxRate(order.Id, 0m), CancellationToken.None);

        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("*verified state tax certificate*");
        order.TaxRate.Should().Be(DefaultRate);
    }

    [Fact]
    public async Task Update_to_a_different_rate_with_a_verified_certificate_applies_and_audits()
    {
        await SeedTaxDocumentAsync(TaxDocumentStatus.Verified, null);
        var (handler, order) = UpdateHandler(DefaultRate);

        await handler.Handle(UpdateTaxRate(order.Id, 0m), CancellationToken.None);

        order.TaxRate.Should().Be(0m);
        OverrideAudits().Should().ContainSingle();
    }

    [Fact]
    public async Task Update_back_to_the_default_rate_passes_without_a_certificate()
    {
        var (handler, order) = UpdateHandler(0m);

        await handler.Handle(UpdateTaxRate(order.Id, DefaultRate), CancellationToken.None);

        order.TaxRate.Should().Be(DefaultRate);
        OverrideAudits().Should().BeEmpty();
    }

    [Fact]
    public async Task Update_resending_the_current_rate_does_not_recheck_the_certificate()
    {
        var (handler, order) = UpdateHandler(0m);

        await handler.Handle(UpdateTaxRate(order.Id, 0m), CancellationToken.None);

        order.TaxRate.Should().Be(0m);
        OverrideAudits().Should().BeEmpty();
    }

    [Fact]
    public async Task Estimate_conversion_fills_the_quote_tax_rate_from_the_customer_default()
    {
        var customer = new Customer { Id = CustomerId, Name = "Tax Co" };
        _db.Customers.Add(customer);
        var estimate = new Quote
        {
            Type = QuoteType.Estimate,
            Title = "Estimate",
            CustomerId = CustomerId,
            Status = QuoteStatus.Sent,
            EstimatedAmount = 500m,
        };
        _db.Quotes.Add(estimate);
        await _db.SaveChangesAsync();

        var quoteRepo = new Mock<IQuoteRepository>();
        quoteRepo.Setup(r => r.GenerateNextQuoteNumberAsync(It.IsAny<CancellationToken>())).ReturnsAsync("QUO-0009");
        var handler = new ConvertEstimateToQuoteHandler(
            _db, quoteRepo.Object, Mock.Of<IPartRepository>(), taxGuard: Guard());

        var result = await handler.Handle(new ConvertEstimateToQuoteCommand(estimate.Id), CancellationToken.None);

        _db.Quotes.Single(q => q.Id == result.Id).TaxRate.Should().Be(DefaultRate);
    }
}
